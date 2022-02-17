using System;

namespace Lilac.ProjectMeme
{
	public class Node
	{
		// 座標
		public (int, int) Pos {get; private set;}

		// 重み。この座標を通るのにかかるコスト
		public int Weight {get; private set;}

		// 衝突タイプ
		public ColliderType ColliderType {get; private set;}

		// この座標に辿り着くのにかかる最低コスト
		public int cost = Int32.MaxValue;

		// 最低コストになる繋ぎ元の座標
		public Node parentNode;

		// 目的地までの推定コスト
		public int heuristicCost;

		// 調査状態
		public NodeStatus status = NodeStatus.None;

		// 優先度スコア。小さい方が有利
		public int Score { get { return cost + heuristicCost; } }


		/// <summary>
		/// コンストラクタ
		/// </summary>
		public Node((int, int) pos, ColliderType colliderType)
		{
			Pos = pos;
			ColliderType = colliderType;
		}


		/// <summary>
		/// コンストラクタ
		/// </summary>
		public Node((int, int) pos, int weight, ColliderType colliderType)
		{
			Pos = pos;
			Weight = weight;
			ColliderType = colliderType;
		}
	}
}