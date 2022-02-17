namespace Lilac.ProjectMeme
{
	/// <summary>
	/// 経路探索アルゴリズムの種類
	/// </summary>
	public enum RoutingAlgorithm
	{
		Simple,			// xy座標比較のみのシンプルなもの
		AStar,			// A*アルゴリズム
		Dijkstra		// ダイクストラ法
	}

	/// <summary>
	/// ノードの調査状態
	/// </summary>
	public enum NodeStatus
	{
		None,		// 未調査
		Open,		// 調査中
		Closed		// 調査完了
	}

	public static class RoutingCst
	{
		// 基準となるノードの重み
		public const int DefNodeWeight = 10;
	}
}