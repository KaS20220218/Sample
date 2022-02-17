using System;
using System.Collections.Generic;

namespace Lilac.ProjectMeme.Field.Routing
{
	public static class RoutingUtil
	{
		/// <summary>
		/// 経路探索を行う矩形の座標の情報を構築する
		/// </summary>
		/// <param name="rect">経路探索を行う矩形</param>
		/// <param name="start">開始座標</param>
		/// <returns>x座標、y座標、ノード</returns>
		public static Dictionary<(int, int), Node> MakeNodeMap(Rect2D rect, (int, int) start,
			bool IsUnitObstacle)
		{
			var nodeDict = new Dictionary<(int, int), Node>();

			for(int x = rect.MinX; x <= rect.MaxX; x++)
			{
				for(int y = rect.MinY; y <= rect.MaxY; y++)
				{
					var pos = ( x, y );

					// 座標の衝突タイプを取得する
					 var colliderType = FieldMng.FieldBo.CalcGridColliderType(pos, IsUnitObstacle);
					
					// 座標を調べて重みを取得する
					int weight = GetWeight(pos, colliderType);

					nodeDict.Add(pos, new Node(pos, weight, colliderType));
				}
			}

			// 開始座標は重みなし、衝突なしとする
			nodeDict[start] = new Node(start, 0, ColliderType.None);

			return nodeDict;
		}


		/// <summary>
		/// 座標を調べて重みを取得する
		/// </summary>
		private static int GetWeight((int, int) pos, ColliderType colliderType)
		{
			int weight = 0;

			// ブロックされているマスは通れないものとする
			switch(colliderType)
			{
				case ColliderType.BlockHalf:
				case ColliderType.Block:
				case ColliderType.Fence:
					weight = Int32.MaxValue;
					break;

				default:
					weight = RoutingCst.DefNodeWeight;
					break;
			}

			// 座標が塞がれていないなら、タイルを調べて重みに反映する
			if(weight != Int32.MaxValue)
			{
				var tile = FieldMng.FieldBo.GetGrid(pos).TileBn;
			}

			return weight;
		}

		
		/// <summary>
		/// 座標から座標へ移動できるかを調べる
		/// </summary>
		/// <returns>true：移動可能</returns>
		public static bool IsPassable(UnitBo walker, Dictionary<(int, int), Node> nodeMap,
			(int, int) start, (int, int) destination)
		{
			if(destination == default)
			{
				return false;
			}

			var gridColliderType = nodeMap[destination].ColliderType;

			// 目的地が通過不能か
			if(!(gridColliderType == ColliderType.Hole && walker.ObjBean.IsFloating)
				&& gridColliderType != ColliderType.None)
			{
				return false;
			}

			// 上下左右への移動は可能である
			if(start.Item1 == destination.Item1 || start.Item2 == destination.Item2)
			{
				return true;
			}

			int diffX = destination.Item1 - start.Item1;
			int diffY = destination.Item2 - start.Item2;

			// 進路の両脇にある座標の衝突タイプを調べる
			if(diffX < 0 && diffY < 0)          // 左下への移動
			{
				return nodeMap[(start.Item1 - 1, start.Item2)].ColliderType != ColliderType.Block
					&& nodeMap[(start.Item1, start.Item2 - 1)].ColliderType != ColliderType.Block;
			}
			else if(diffX > 0 && diffY < 0)     // 右下への移動
			{
				return nodeMap[(start.Item1 + 1, start.Item2)].ColliderType != ColliderType.Block
					&& nodeMap[(start.Item1, start.Item2 - 1)].ColliderType != ColliderType.Block;
			}
			else if(diffX < 0 && diffY > 0)     // 左上への移動
			{
				return nodeMap[(start.Item1 - 1, start.Item2)].ColliderType != ColliderType.Block
					&& nodeMap[(start.Item1, start.Item2 + 1)].ColliderType != ColliderType.Block;
			}
			else                                // 右上への移動
			{
				return nodeMap[(start.Item1 + 1, start.Item2)].ColliderType != ColliderType.Block
					&& nodeMap[(start.Item1, start.Item2 + 1)].ColliderType != ColliderType.Block;
			}
		}


		/// <summary>
		/// ノードに辿り着くまでのコストを計算する
		/// </summary>
		/// <param name="node">対象ノード</param>
		/// <param name="newParent">新しい親ノード</param>
		public static void CalcCost(Node node, Node newParent)
		{
			int newCost = newParent.cost + node.Weight;

			// よりコストが少ないなら情報を更新する
			if(newCost < node.cost)
			{
				node.cost = newCost;
				node.parentNode = newParent;
			}
		}


		/// <summary>
		/// ノードから目的地までの推定コストを計算する。起点ノードのコストは含めない
		/// </summary>
		public static void CalcHeuristicCost(Node startNode, (int, int) destination)
		{
			int distance = Math.Abs(destination.Item1 - startNode.Pos.Item1) +
				Math.Abs(destination.Item2 - startNode.Pos.Item2);

			startNode.heuristicCost = RoutingCst.DefNodeWeight * distance;
		}
	}
}