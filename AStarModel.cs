using Lilac.Util;
using System.Collections.Generic;

namespace Lilac.ProjectMeme.Field.Routing
{
	public static class AStarModel
	{
		/// <summary>
		/// 目的地までの経路の最初の座標を求める
		/// </summary>
		/// <returns>経路がなければnull</returns>
		public static (int, int) NextPos(UnitBo walker, (int, int) start, (int, int) destination, bool IsUnitObstacle,
			WanderData wanderDat)
		{
			// 経路を求める
			wanderDat.path = CalcRoute(walker, start, destination, null, IsUnitObstacle);

			// 経路があるなら最初の座標を返す
			return wanderDat.path.Count > 0 ? wanderDat.path[0] : default;
		}


		/// <summary>
		/// 経路を求める
		/// </summary>
		public static List<(int, int)> CalcRoute(UnitBo walker, (int, int) start, (int, int) destination, Rect2D rect,
			bool IsUnitObstacle)
		{
			var routePosList = new List<(int, int)>();
			
			// 開始地や目的地がない、または開始地と目的地が同じなら処理しない
			if(start == default || destination == default || GeneralUtil.IsEqual(start, destination))
			{
				return routePosList;
			}

			// 範囲を指定されてないならフィールド全体とする
			if(rect == null)
			{
				rect = new Rect2D(FieldCst.MinMapPosX, FieldCst.MinMapPosY,
					FieldMng.FieldBo.MaxX, FieldMng.FieldBo.MaxY);
			}

			// 経路探索を行う矩形の座標の情報を構築する
			var nodeMap = RoutingUtil.MakeNodeMap(rect, start, IsUnitObstacle);

			// 目的地を開始地点とする。目的地から探索することで、最後にノードを繋げやすくなる
			var node = nodeMap[destination];

			// 開始地点のコストは0とする
			node.cost = 0;

			// スコアを昇順に並べたノード探索キュー。key：スコア、value：ノード
			var sortedScoreDict = new SortedDictionary<int, List<Node>>();

			bool reachedDestination = false;

			// 目的地に着くか、探索するノードがなくなったら処理を終える
			while(!reachedDestination && node != null)
			{
				// 対象ノードは調査完了とする
				node.status = NodeStatus.Closed;
				
				// 対象ノードの周囲のノードの調査を開始する
				reachedDestination = OpenNodes(walker, node, start, rect, nodeMap, sortedScoreDict);
				
				// 最小コストのノードを取得する
				node = GetMinScoreNode(sortedScoreDict);
			}
			
			if(node != null)
			{
				// ノード数は座標数を上回らない
				int maxNodeNum = rect.MaxX * rect.MaxY;

				// 親を辿っていけばルートとなる
				while(true)
				{
					node = node.parentNode;

					if(node == null)
					{
						break;
					}

					routePosList.Add(node.Pos);

					if(routePosList.Count > maxNodeNum)
					{
						UnityEngine.Debug.LogFormat("<color=red>A* Algorithm, got excessive Node number:" +
							"rect.MaxX = {0}, rect.MaxY = {1}, node numebr = {2}</color>",
							rect.MaxX, rect.MaxY, routePosList.Count);
						break;
					}
				}
			}
			
			return routePosList;
		}


		/// <summary>
		/// 対象ノードの周囲のノードの調査を開始する
		/// </summary>
		/// <param name="node">対象ノード</param>
		/// <param name="destination">目的地</param>
		/// <param name="rect">探索範囲の矩形</param>
		/// <param name="nodeMap">全ノードの情報</param>
		/// <param name="sortedScoreDict">コストを昇順に並べたノード探索キュー</param>
		/// <returns>true：目的地にたどり着いた</returns>
		private static bool OpenNodes(UnitBo walker, Node node, (int, int) destination, Rect2D rect, 
			Dictionary<(int, int), Node> nodeMap, SortedDictionary<int, List<Node>> sortedScoreDict)
		{
			bool reachedDestination = false;
			
			// 周囲の座標を取得する
			foreach(var nextPos in FieldMng.GetMapPosFrom(node.Pos, 1, rect))
			{
				var nextNode = nodeMap[nextPos];
				
				// 両脇の座標のどちらかがブロックされているなら調査しない
				if(!RoutingUtil.IsPassable(walker, nodeMap, node.Pos, nextNode.Pos))
				{
					continue;
				}
				
				// 目的地に着いた時点で処理を終了する
				if(nextNode.Pos.Item1 == destination.Item1 && nextNode.Pos.Item2 == destination.Item2)
				{
					// 取り出すために通常では到達不可な最小スコアで辞書に入れる
					sortedScoreDict.Add(-1, new List<Node>() { nextNode });

					nextNode.parentNode = node;
					reachedDestination = true;
					break;
				}

				// 調査を終えたノードなら調査しない
				if(nextNode.status == NodeStatus.Closed)
				{
					continue;
				}

				// 空白座標でない
				// かつ歩行者が浮遊していて穴の座標でないなら調査しない
				if(nextNode.ColliderType != ColliderType.None 
					&& !(walker.ObjBean.IsFloating && nextNode.ColliderType == ColliderType.Hole))
				{
					continue;
				}
				
				// コストを計算する
				RoutingUtil.CalcCost(nextNode, node);

				// ノードから目的地までの推定コストを計算する
				RoutingUtil.CalcHeuristicCost(nextNode, destination);
				
				// 未着手のノードでないなら辞書に入れない
				if(nextNode.status != NodeStatus.None)
				{
					continue;
				}
				
				// 探索に入ったことを記録する
				nextNode.status = NodeStatus.Open;

				// スコアの小さい順に取り出すために辞書に入れる
				List<Node> nodes = null;

				if(!sortedScoreDict.TryGetValue(nextNode.Score, out nodes))
				{
					nodes = new List<Node>();
					sortedScoreDict.Add(nextNode.Score, nodes);
				}
				
				nodes.Add(nextNode);
			}

			return reachedDestination;
		}


		/// <summary>
		/// 最小コストのノードを取得する
		/// </summary>
		private static Node GetMinScoreNode(SortedDictionary<int, List<Node>> sortedScoreDict)
		{
			Node node = null;
			int score = 0;

			// 最小スコアのノードから1つをランダムに選ぶ
			foreach(var pair in sortedScoreDict)
			{
				score = pair.Key;
				node = pair.Value[UnityEngine.Random.Range(0, pair.Value.Count)];
				pair.Value.Remove(node);
				break;
			}

			List<Node> nodes = null;

			// このスコアのノードが全て調査済みになったら辞書からスコアを消す
			if(sortedScoreDict.TryGetValue(score, out nodes) && nodes.Count == 0)
			{
				sortedScoreDict.Remove(score);
			}

			return node;
		}
	}
}