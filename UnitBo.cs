using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Lilac.Util;
using System;
using UniRx;
using System.Xml.Linq;
using DG.Tweening;
using System.Text;

namespace Lilac.ProjectMeme
{
	public class UnitBo : AObject
	{
		// アイテムデータクラス
		public UnitBean UnitBn { get; private set; }

		// 基本防御情報
		public ArmorBean ArmorBn { get; private set; }

		// AI
		public UnitAi Ai { get; private set; }

		// 持ち物
		private List<ItemBo> inventory = new List<ItemBo>();
		public IReadOnlyList<ItemBo> Inventory { get { return inventory; } }

		// 行動したフラグ
		public bool alreadyActed;

		// アクション中フラグ
		public bool IsActing { get; protected set; }

		// 仕掛け起動フラグ
		public bool IsToActivateMcn { get; protected set; }

		// ターン中フラグ
		public bool IsOnTurn { get; protected set; }

		// ターン終了中フラグ
		public bool IsEndingTurn { get; protected set; }

		// 状態異常リスト
		private List<StatusEffectBo> statusEffectBoList = new List<StatusEffectBo>();
		public IReadOnlyList<StatusEffectBo> StatusEffectList { get { return statusEffectBoList; } }

		// 状態異常リストのコピー（ターン終了処理用）
		private List<StatusEffectBo> copiedStatusEffectBoList;

		// 向いている方向
		public Direction Dir { get; private set; } = Direction.Down;

		// 素手。武器を持たないときに武器となる
		public ItemBo BareHands { get; protected set; }

		// key:部位ID、value:装備
		public Dictionary<EquipPartBo, ItemBo> equipmentDict = new Dictionary<EquipPartBo, ItemBo>(9);
		public List<ItemBo> equipmentList = new List<ItemBo>(9);

		// アニメーション速度倍率
		protected float DefAniSpd = AnimationCst.AniSpdDefault;
		private float aniSpd = AnimationCst.AniSpdDefault;

		// 現レベル
		public int Lvl { get; private set; }

		// 所持記憶子
		public int Memorino { get; private set; }

		// 使用可能なスキルIDリスト
		private List<SkillId> memorizedSkills;
		public List<(SkillId, ItemBo)> SkillList 
		{ 
			get
			{
				var list = new List<(SkillId, ItemBo)>(memorizedSkills.Count + 3 * 2);

				// 記憶しているスキル
				foreach(var skillId in memorizedSkills)
				{
					Add(skillId, null);
				}

				// 武器からのスキル
				foreach(var pair in GetMeleeWeapons())
				{
					Add(pair.Item2.WeaponBn.Skill1, pair.Item2);
					Add(pair.Item2.WeaponBn.Skill2, pair.Item2);
					Add(pair.Item2.WeaponBn.Skill3, pair.Item2);
				}

				return list;

				/// <summary>
				/// スキルIDをリストに加える
				/// </summary>
				void Add(SkillId skillId, ItemBo weapon)
				{
					if(skillId != default)
					{
						list.Add((skillId, weapon));
					}
				}
			}
		}

		// 使用中のスキル
		public SkillBo usingSkill;

		// 使用中の常駐スキル
		private List<ASustainSkillBo> sustainSkills = new List<ASustainSkillBo>();

		// true: 待機中
		public bool IsOnDelay { get { return delaySecond > 0; } }

		// 待機時間
		private float delaySecond;
		public float DelaySecond
		{
			set
			{
				if(value == 0 || value > delaySecond)
				{
					delaySecond = value;
				}
			}
		}

		// アニメイベント
		private AnimEvt _animEvt;
		private AnimEvt animEvt
		{
			get
			{
				if(_animEvt == null)
				{
					_animEvt = uom.gameObject.GetComponent<AnimEvt>();
				}

				return _animEvt;
			}
		}

		// true: 死んでいる
		public bool isDead;

		// true: 死亡処理済み
		public bool dieProcessed;

		// true: ヒット時にアニメーションではなく振動する
		public bool shakeOnHit;

		// ターン終了メイン処理の終了が通知された回数
		private int informCount;

		// 部位リスト
		public List<BodyPartBo> BodyParts { get; private set; }
		private List<EquipPartBo> equipParts;

		// ダメージ計算時のターン処理に関するフラグ
		private DmgProcessFlags dmgProcessFlags;

		// 現ターン内で残っている攻撃処理の数
		private int remainedAtkPrcsNum;


		/// <summary>
		/// 位置が変わった時に行う処理
		/// </summary>
		protected override void WhenPosChanged(bool updateFov)
		{
			base.WhenPosChanged(updateFov);

			if(updateFov && IsPlayer())
			{
				// プレイヤーならFOVを更新する
				SightMng.UpdateFieldOfView();
			}
			else
			{
				// ユニットを可視・不可視化は必ず行う
				SightMng.SetUnitVisibility();
			}
		}


		/// <summary>
		/// ダメージを受けた後のモジュールの処理
		/// </summary>
		protected void AfterTakeDmgModule(DmgBo dmgBo)
		{
			foreach(var module in modules)
			{
				module.AfterTakeDmg(dmgBo);
			}

			if(dmgBo.Attacker != null && Ai.IsFavorType(dmgBo.Attacker, FavorType.Hostile))
			{
				Ai.hate = dmgBo.Attacker;
			}
		}


		/// <summary>
		/// コンストラクタ
		/// </summary>
		public UnitBo(UnitBean ub)
		{
			// パラメータ
			Lvl = 1;

			// データを保存する
			UnitBn = ub;
			ArmorBn = EquipmentMng.GetArmorBean(ub.ObjId);

			// 部位をセットする
			BodyParts = UnitMng.GetBodyParts(ub.BodyPartsId);
			equipParts = UnitMng.GetEquipParts(ub.EquipPartsId);

			// データをセットする
			UnitBn.idvFavor = CampMng.GetCampFavorToPc(UnitBn.CampId);

			// 素手のパラメータをセットする
			BareHands = new ItemBo(EquipmentMng.GenerateWeaponBareHands(this));

			// レベルに応じた習得可能なスキルを取得する
			memorizedSkills = UnitMng.GetUnitSkillBean(UnitBn.ObjId).GetUsableSkills(Lvl);

			// 満腹状態を計算する
			CalcSatietyStatus();

			// AIを初期化する
			InitUnitAi();
		}


		/// <summary>
		/// コンストラクタ
		/// </summary>
		public UnitBo(XElement saveData)
		{
			// パラメータ
			Lvl = SaveMng.GetIntVal(saveData, SaveCst.SdeLvl);
			Memorino = SaveMng.GetIntVal(saveData, SaveCst.SdeMemorino);

			// データを保存する
			UnitBn = new UnitBean(saveData);
			ArmorBn = EquipmentMng.GetArmorBean(UnitBn.ObjId);

			// 部位をセットする
			BodyParts = UnitMng.GetBodyParts(UnitBn.BodyPartsId);
			var bodyPartsXe = saveData.Element(SaveCst.SdeBps);
			BodyParts.ForEach(x => { x.InputSaveData(bodyPartsXe); });
			equipParts = UnitMng.GetEquipParts(UnitBn.EquipPartsId);

			// 素手のパラメータをセットする
			BareHands = new ItemBo(EquipmentMng.GenerateWeaponBareHands(this));

			// レベルに応じた習得可能なスキルを取得する
			memorizedSkills = new List<SkillId>(10);

			foreach(var element in saveData.Element(SaveCst.SdeSkills).Elements(SaveCst.SdeSkillId))
			{
				memorizedSkills.Add(GeneralUtil.StrToEnum<SkillId>(element.Value));
			}

			// 満腹状態を計算する
			CalcSatietyStatus();

			// AIを初期化する
			InitUnitAi();
		}


		/// <summary>
		/// コンストラクタ
		/// </summary>
		public UnitBo(UnitBo unit)
		{
			UnitBn = GeneralUtil.DeepCopy(unit.UnitBn);
			ArmorBn = EquipmentMng.GetArmorBean(UnitBn.ObjId);
			Lvl = unit.Lvl;

			// 部位をセットする
			BodyParts = UnitMng.GetBodyParts(UnitBn.BodyPartsId);
			equipParts = UnitMng.GetEquipParts(UnitBn.EquipPartsId);
		}


		/// <summary>
		/// ユニットのxml要素を作成する
		/// </summary>
		public XElement MakeSaveDataUnit()
		{
			// Unit要素
			var xe = new XElement(SaveCst.SdeUnit);

			// ユニットID
			var eId = new XElement(SaveCst.SdeObjId, UnitBn.ObjId);
			xe.Add(eId);

			// 性別
			xe.Add(new XElement(SaveCst.SdeSex, (int) UnitBn.sex));

			// 速度
			xe.Add(new XElement(SaveCst.SdeSpeed, (int) UnitBn.Speed));

			// 生命力
			xe.Add(new XElement(SaveCst.SdeVitality, UnitBn.Vitality));

			// 精神力
			xe.Add(new XElement(SaveCst.SdeSpirit, UnitBn.Spirit));

			// 持久力
			xe.Add(new XElement(SaveCst.SdeStamina, UnitBn.Stamina));

			// 親和性
			xe.Add(new XElement(SaveCst.SdeAttunement, UnitBn.Attunement));

			// 筋力
			xe.Add(new XElement(SaveCst.SdeStrength, UnitBn.Strength));

			// 器用
			xe.Add(new XElement(SaveCst.SdeDexterity, UnitBn.Dexterity));

			// 神秘
			xe.Add(new XElement(SaveCst.SdeArcane, UnitBn.Arcane));

			// 基礎命中
			xe.Add(new XElement(SaveCst.SdeDefAccuracy, UnitBn.Accuracy));

			// 満腹度
			xe.Add(new XElement(SaveCst.SdeSatCostCoeff, UnitBn.satietyCostCoeff));
			xe.Add(new XElement(SaveCst.SdeMaxSatiety, UnitBn.maxSatiety));
			xe.Add(new XElement(SaveCst.SdeSatiety, UnitBn.satiety));

			// 潤い
			xe.Add(new XElement(SaveCst.SdeWaterCostCoeff, UnitBn.waterCostCoeff));
			xe.Add(new XElement(SaveCst.SdeMaxWater, UnitBn.maxWater));
			xe.Add(new XElement(SaveCst.SdeWater, UnitBn.water));

			// 個人好感度
			xe.Add(new XElement(SaveCst.SdeIdvFavor, UnitBn.idvFavor));

			// 陣営ID
			xe.Add(new XElement(SaveCst.SdeCampId, UnitBn.CampId));

			// 肖像画ID
			xe.Add(new XElement(SaveCst.SdePortraitId, UnitBn.PortraitId));

			// 異名
			xe.Add(new XElement(SaveCst.SdeAliasType, (int) UnitBn.AliasType));

			if(UnitBn.AliasType == AliasType.Fixed)
			{
				xe.Add(new XElement(SaveCst.SdeAlias, UnitBn.Alias));
				xe.Add(new XElement(SaveCst.SdeIsAdjective, FileUtil.BoolToStrVal(UnitBn.IsAdjective)));
			}

			// 名前
			xe.Add(new XElement(SaveCst.SdeNameStatus, (int) UnitBn.NameStatus));

			if(UnitBn.NameStatus == NameStatus.Fixed)
			{
				xe.Add(new XElement(SaveCst.SdeName, UnitBn.Name));
			}

			// 持ち物
			xe.Add(MakeSaveDataInventory());

			// 装備
			xe.Add(MakeSaveDataEquipment());

			// 所持スキル
			xe.Add(MakeSaveDataSkills());

			// レベル
			xe.Add(new XElement(SaveCst.SdeLvl, Lvl));

			// 所持記憶子
			xe.Add(new XElement(SaveCst.SdeMemorino, Memorino));

			// 常駐スキルのセーブデータを作成する
			xe.Add(MakeSaveDataSustainSkill());

			// モジュールのセーブデータを作る
			xe.Add(MakeModulesSaveData());

			// AI
			xe.Add(Ai.MakeSaveData());

			// 身体部位
			xe.Add(MakeSaveDataBodyParts());

			return xe;


			/// <summary>
			/// ユニットの持ち物の要素を作る
			/// </summary>
			XElement MakeSaveDataInventory()
			{
				var xeInventory = new XElement(SaveCst.SdeInventory);

				foreach(var item in Inventory)
				{
					// アイテムのxml要素を作成する
					xeInventory.Add(item.MakeSaveData());
				}

				return xeInventory;
			}

			/// <summary>
			/// ユニットの装備の要素を作る
			/// </summary>
			XElement MakeSaveDataEquipment()
			{
				var xeEquipment = new XElement(SaveCst.SdeEquipments);

				// 装備可能な部位を取得する
				var equipableParts = GetEquipableParts();

				for(int i = 0; i < equipableParts.Count; i++)
				{
					// 指定部位に装備しているアイテムを取得する
					var equipment = GetEquipmentFromPart(equipableParts[i]);

					if(equipment == null)
					{
						continue;
					}

					var equipmentXe = new XElement(SaveCst.SdeEquipment);
					xeEquipment.Add(equipmentXe);

					// 装備品のインデックスを記録する
					equipmentXe.Add(new XElement(SaveCst.SdeIndex, i.ToString()));

					// アイテムのxml要素を作成する
					equipmentXe.Add(equipment.MakeSaveData());
				}

				return xeEquipment;
			}


			/// <summary>
			/// ユニット所持スキルの要素を作る
			/// </summary>
			XElement MakeSaveDataSkills()
			{
				var xeSkills = new XElement(SaveCst.SdeSkills);

				foreach(var skillId in memorizedSkills)
				{
					xeSkills.Add(new XElement(SaveCst.SdeSkillId, (int) skillId));
				}

				return xeSkills;
			}

			/// <summary>
			/// 身体部位の要素を作る
			/// </summary>
			XElement MakeSaveDataBodyParts()
			{
				var xeBps = new XElement(SaveCst.SdeBps);

				foreach(var bpb in BodyParts)
				{
					xeBps.Add(bpb.MakeSaveData());
				}

				return xeBps;
			}
		}


		/// <summary>
		/// GameObject生成後の初期化処理
		/// </summary>
		public void InitAfterGenerateGameObject(XElement saveData)
		{
			// 常駐スキルのセーブデータを読み込む
			ReadSaveDataSustainSkill(saveData);
		}


		/// <summary>
		/// ランダムを指定されている値を固定する
		/// </summary>
		public void FixRandom()
		{
			// エディタならランダムのままにする
			if(SceneMng.IsEditor())
			{
				return;
			}

			UnitBn.FixRandom(this);

			var humanMdl = GetModule<HumanMdl>();

			if(humanMdl != null)
			{
				humanMdl.FixRandom();
			}
		}


		/// <summary>
		/// ターンを開始する
		/// </summary>
		public void StartTurn()
		{
			//Debug.LogFormat("<color=yellow>{0}, {1}, {2}</color>", GetName(), IsOnTurn, IsActing);

			// 既にターンが始まっている、もしくはまだ前ターンのアクション中なら新しいターンを始めない
			if(IsOnTurn || IsActing)
			{
				return;
			}

			//Debug.LogFormat("<color=yellow>StartTurn() start, {0}, {1}</color>", GetName(), Time.frameCount);

			IsOnTurn = true;
			alreadyActed = false;

			// 前ターンのフラグをクリアする
			dmgProcessFlags = new DmgProcessFlags();

			// 状態異常のターン開始時の処理
			foreach(var seBo in new List<StatusEffectBo>(statusEffectBoList))
			{
				seBo.OnStartTurn();
			}

			if(IsPlayer())
			{
				// プレイヤーならFOVを更新する
				SightMng.UpdateFieldOfView();

				Delay(() =>
				{
					// 精神崩壊、麻痺なら操作不可
					if(HasStatusEffect(StatusEffect.MentalCollapse) 
						|| HasStatusEffect(StatusEffect.Paralyze))
					{
						Ai.PlayTurn();
					}
				});
			}
			else
			{
				// ターンラプス中ならアニメーション速度を速める
				if(TurnMng.IsInTurnlapse)
				{
					ChangeAniSpd(AnimationCst.AniSpdTurnlapse);
				}

				Delay(() =>
				{
					Ai.PlayTurn();
				});
			}

			//Debug.LogFormat("<color=yellow>{0}, StartTurn() end</color>", GetName());
		}


		/// <summary>
		/// ターン終了処理を開始する
		/// </summary>
		public void StartEndingTurn()
		{
			//Debug.LogFormat("<color=red>{0}, {1}, EndTurn() ?</color>", GetName(), IsOnTurn);

			// ターンが既に終了しているなら処理しない
			if(!IsOnTurn || IsEndingTurn)
			{
				return;
			}

			//Debug.LogFormat("<color=red>{0}, {1}, EndTurn() start</color>", GetName(), IsOnTurn);

			IsEndingTurn = true;

			// 状態異常リストをコピーする
			copiedStatusEffectBoList = new List<StatusEffectBo>(statusEffectBoList);

			informCount = 0;

			// ターン終了処理のメイン部分が終わったことを通知する
			CompletedEndTurnMainProcess();
		}


		/// <summary>
		/// ターン終了処理のメイン部分が終わったことを通知する
		/// </summary>
		public void CompletedEndTurnMainProcess()
		{
			informCount++;
			
			if(copiedStatusEffectBoList.Count >= informCount)
			{
				// 未処理の状態異常があるならターン終了前に処理する
				copiedStatusEffectBoList[informCount - 1].OnEndTurn();

				return;
			}

			// 暫定：プレイヤーなら処理しない
			// AIのターン終了処理
			if(!IsPlayer())
			{
				Ai.EndTurn();
			}

			if(IsPlayer())
			{
				// プレイヤーならFOVを更新する
				SightMng.UpdateFieldOfView();
			}
			else
			{
				// ユニットを可視・不可視化は必ず行う
				SightMng.SetUnitVisibility();
			}

			// 待ち時間を加算する
			UnitBn.waitTime += GetWaitTimePerTurn();

			Delay(() =>
			{
				Ai.isPlaying = false;
				IsOnTurn = false;
				IsEndingTurn = false;

				//Debug.LogFormat("<color=red>{0}, {1}, {2}, EndTurn() end</color>", GetName(), Time.frameCount, UnitBn.waitTime);

				// ターンを切り替える
				TurnMng.SwitchTurn();
			});
		}


		/// <summary>
		/// ターン毎に加算するWTを求める
		/// </summary>
		public int GetWaitTimePerTurn()
		{
			// 鈍足の状態異常の個数
			int slowCount = GetStatusEffectNum(StatusEffect.Slow);

			// 実際の速度
			int actualSpeed = ((int) UnitBn.Speed) / (slowCount + 1);
			actualSpeed = actualSpeed < (int) SpeedLevel.x025 ? (int) SpeedLevel.x025 : actualSpeed;

			return UnitCst.WaitTimex025 / actualSpeed;
		}


		/// <summary>
		/// 時間単位ごとの処理
		/// </summary>
		public override bool ProcessPerTimeUnit(int passedTime)
		{
			if(!TurnMng.TurnLockDat.FinishedUnitBoPptuBase)
			{
				if(!base.ProcessPerTimeUnit(passedTime))
				{
					return false;
				}

				TurnMng.TurnLockDat.FinishedUnitBoPptuBase = true;
			}

			if(!TurnMng.TurnLockDat.FinishedUnitBoPptuInventory)
			{
				// 持ち物のアイテムの時間経過処理
				for(int i = 0; i < inventory.Count; i++)
				{
					if(i < TurnMng.TurnLockDat.loopUnitBoPptuInventory)
					{
						continue;
					}

					if(!inventory[i].ProcessPerTimeUnit(passedTime))
					{
						return false;
					}

					TurnMng.TurnLockDat.loopUnitBoPptuInventory++;
				}

				TurnMng.TurnLockDat.FinishedUnitBoPptuInventory = true;
			}

			if(!TurnMng.TurnLockDat.FinishedUnitBoPptuEquipment)
			{
				// 装備品の時間経過処理
				for(int i = 0; i < equipmentList.Count; i++)
				{
					if(i < TurnMng.TurnLockDat.loopUnitBoPptuEquipment)
					{
						continue;
					}

					if(!equipmentList[i].ProcessPerTimeUnit(passedTime))
					{
						return false;
					}

					TurnMng.TurnLockDat.loopUnitBoPptuEquipment++;
				}

				TurnMng.TurnLockDat.FinishedUnitBoPptuEquipment = true;
			}

			// 待ち時間更新
			UnitBn.waitTime -= passedTime;

			// プレイヤーなら
			if(IsPlayer())
			{
				// 経過秒分の満腹度と潤い値を消費する
				var costPerSec = UnitCst.SatietyCostPerSec * (passedTime / TimeCst.OneSecond);
				ConsumeSatiety(costPerSec);
				ConsumeWater(costPerSec);
			}

			// 一度のターン間処理で何度も使うため初期化
			TurnMng.TurnLockDat.FinishedUnitBoPptuBase = false;
			TurnMng.TurnLockDat.FinishedUnitBoPptuInventory = false;
			TurnMng.TurnLockDat.loopUnitBoPptuInventory = 0;
			TurnMng.TurnLockDat.FinishedUnitBoPptuEquipment = false;
			TurnMng.TurnLockDat.loopUnitBoPptuEquipment = 0;

			return true;
		}


		/// <summary>
		/// 移動可能なら移動する
		/// </summary>
		/// <returns>true:歩行成功, false:歩行失敗</returns>
		public bool TryWalk((int, int) nextPos, bool endTurnWhenCantWalk, bool alreadyCheckPassable = false)
		{
			return TryWalk(
				FieldMng.GetDirection(MapPos, nextPos), endTurnWhenCantWalk, alreadyCheckPassable);
		}


		/// <summary>
		/// 移動可能なら移動する
		/// </summary>
		/// <param name="endTurnWhenCantWalk">true: 移動できなくてもターン終了</param>
		/// <param name="alreadyCheckPassable">既に目的地が通行可能かを調べたフラグ</param>
		/// <returns>true:歩行成功, false:歩行失敗</returns>
		public bool TryWalk(Direction dir, bool endTurnWhenCantWalk, bool alreadyCheckPassable = false)
		{
			bool hasWalked = false;

			// 足踏み
			if(dir == Direction.None)
			{
				if(IsPlayer())
				{
					// プレイヤーの場合のみターン終了までにワンテンポ置く
					IsActing = true;

					DOVirtual.DelayedCall(0.2f, () =>
					{
						IsActing = false;
						StartEndingTurn();
					});
				}
				else
				{
					StartEndingTurn();
				}

				return hasWalked;
			}

			// 可能であれば外のシーンに遷移する
			TryGoToOut();

			if(alreadyCheckPassable || IsPassable(dir))
			{
				// 移動する
				Walk(dir);

				hasWalked = true;
			}
			else
			{
				// 衝突したオブジェクトを取得する
				var obstacle = FieldMng.FieldBo.GetObstacle(GetFrontPos());

				if(dir != Dir)
				{
					// 回転アニメーション終了後の処理
					var animFinished = new Subject<Unit>();

					animFinished.Subscribe(_ =>
					{
						if(obstacle is AObject)
						{
							// 衝突したときの処理
							((AObject) obstacle).WhenCollided(this);
						}
					});

					// 通過可能でなく、方向転換可能なら方向転換のみを行う
					Rotate(dir, true, animFinished);
				}
				else
				{
					// 既に進行方向に向いている場合
					if(obstacle is AObject)
					{
						// 衝突したときの処理
						((AObject) obstacle).WhenCollided(this);
					}
				}

				// プレイヤー以外のユニットはターン終了とする
				if(endTurnWhenCantWalk)
				{
					StartEndingTurn();
				}
			}

			// 行動したフラグを立てる
			alreadyActed = hasWalked;

			return hasWalked;

			/// <summary>
			/// 可能であれば外のシーンに遷移する
			/// </summary>
			void TryGoToOut()
			{
				// プレイヤーで
				// 出口タイプが外で
				// 目的地がマップの範囲外で
				// 通り抜けられるならワールド画面に遷移する
				if(IsPlayer()
					&& SceneMng.CurScene.exitType == ExitType.Outside
					&& !FieldMng.FieldBo.IsPosInMapRange(FieldMng.GetMapPos(dir, MapPos))
					&& IsSidePassable(dir))
				{
					var isd = new InterSceneData();
					isd.tempPos = (SceneMng.CurScene.outMapPosX, SceneMng.CurScene.outMapPosY);

					SceneMng.Transition(isd, TransType.Out);
				}
			}
		}


		/// <summary>
		/// 目的のマスとそのサイドのマスを調べ、通過可能かどうかを判定する
		/// </summary>
		public bool IsPassable((int, int) posAround)
		{
			return IsPassable(FieldMng.GetDirection(MapPos, posAround));
		}


		/// <summary>
		/// 目的のマスとそのサイドのマスを調べ、通過可能かどうかを判定する
		/// </summary>
		public bool IsPassable(Direction dir)
		{
			// 目的のマスに存在可能で、そのサイドのマスが衝突しないタイプなら通過可能である
			return IsDestPassable(dir) && IsSidePassable(dir);
		}


		/// <summary>
		/// 目的のマスを調べ、通過可能かどうかを判定する
		/// </summary>
		/// <returns>true: 通過可能, false: 通過不可</returns>
		protected bool IsDestPassable(Direction dir)
		{
			if(MapPos == default)
			{
				Debug.unityLogger.Log("<color=red>An object's position is null: name = "
					+ uom.name + "</color>");
				return false;
			}

			var newMapPos = MapPos;

			// 目的地の座標を取得する
			switch(dir)
			{
				case Direction.DownLeft:
					newMapPos.Item1--;
					newMapPos.Item2--;
					break;

				case Direction.Down:
					newMapPos.Item2--;
					break;

				case Direction.DownRight:
					newMapPos.Item1++;
					newMapPos.Item2--;
					break;

				case Direction.Left:
					newMapPos.Item1--;
					break;

				case Direction.Right:
					newMapPos.Item1++;
					break;

				case Direction.UpLeft:
					newMapPos.Item1--;
					newMapPos.Item2++;
					break;

				case Direction.Up:
					newMapPos.Item2++;
					break;

				case Direction.UpRight:
					newMapPos.Item1++;
					newMapPos.Item2++;
					break;
			}

			// 指定したマップ座標に存在できるかを調べる
			return ObjectMng.CanStandAt(ObjBean, newMapPos);
		}


		/// <summary>
		/// 両サイドのマスを調べ、通過可能かどうかを判定する
		/// </summary>
		/// <returns>
		/// true: passable, false: impassable
		/// </returns>
		private bool IsSidePassable(Direction dir)
		{
			bool isPassable = true;
			var sidePos = new List<(int, int)>(4);

			// 方向によって調べるマスを変更する
			if(dir == Direction.DownLeft || dir == Direction.DownRight)
			{
				sidePos.Add((MapPos.Item1, MapPos.Item2 - 1));
			}

			if(dir == Direction.DownRight || dir == Direction.UpRight)
			{
				sidePos.Add((MapPos.Item1 + 1, MapPos.Item2));
			}

			if(dir == Direction.UpRight || dir == Direction.UpLeft)
			{
				sidePos.Add((MapPos.Item1, MapPos.Item2 + 1));
			}

			if(dir == Direction.UpLeft || dir == Direction.DownLeft)
			{
				sidePos.Add((MapPos.Item1 - 1, MapPos.Item2));
			}

			// 調べる
			foreach(var pos in sidePos)
			{
				if(!FieldMng.FieldBo.IsPosInMapRange(pos)
					|| FieldMng.FieldBo.CalcGridColliderType(pos) == ColliderType.Block)
				{
					isPassable = false;
					break;
				}
			}

			return isPassable;
		}


		/// <summary>
		/// オブジェクトを歩行させる
		/// </summary>
		/// <param name="isDiagonal">
		/// 斜め移動かどうか
		/// </param>
		/// <param name="dir">
		/// キャラクターの回転する向き
		/// </param>
		private void Walk(Direction dir)
		{
			// 移動方向に向ける
			Rotate(dir, false);

			int newPosX = MapPos.Item1;
			int newPosY = MapPos.Item2;

			// 移動先のマップ座標を計算する
			switch(dir)
			{
				case Direction.DownLeft:
					newPosX--;
					newPosY--;
					break;

				case Direction.Down:
					newPosY--;
					break;

				case Direction.DownRight:
					newPosX++;
					newPosY--;
					break;

				case Direction.Left:
					newPosX--;
					break;

				case Direction.Right:
					newPosX++;
					break;

				case Direction.UpLeft:
					newPosX--;
					newPosY++;
					break;

				case Direction.Up:
					newPosY++;
					break;

				case Direction.UpRight:
					newPosX++;
					newPosY++;
					break;

				default:
					Debug.unityLogger.Log("<color=red>Unexpected direction: </color>" + dir);
					break;
			}

			// 座標変更
			var newPos = (newPosX, newPosY);
			ChangeMapPos(newPos, false);

			// 移動したことを通知する
			UnitEventMng.UnitMoved(this);

			// 目的地のワールド座標
			Vector3 destination = CalcPosAtCertainCoord(newPos);

			var walkFinished = new Subject<Unit>();
			walkFinished.Subscribe(_ =>
			{
				// プレイヤーなら
				if(IsPlayer())
				{
					// 満腹度を消費する
					//ConsumeSatiety(ActionMng.CalcSatietyCost(ActionType.Walk));
				}

				// ターン終了
				StartEndingTurn();
			});

			// 時間をかけて移動を行う
			ActionWalk(destination, walkFinished);
		}


		/// <summary>
		/// このオブジェクトを動かす
		/// </summary>
		private void ActionWalk(Vector3 destination, Subject<Unit> walkFinished)
		{
			IsActing = true;

			// 移動先に仕掛けが存在するかを調べる
			IsToActivateMcn = IsToActivateMachine();

			if(!IsToActivateMcn)
			{
				// 仕掛けがないなら
				// 歩行アニメーションは全てのキャラでほぼ同時に行わせるため、ここでターンを終了する
				StartEndingTurn();
			}
			
			if(Visible)
			{
				// 歩行アニメーション開始
				if(AnimationMng.HasAnimation(AnimatorCompo, AnimationCst.AnimNamePatWalk))
				{
					AnimatorCompo.Play(AnimationCst.StateWalk, 0);
				}

				// 目的地まで時間をかけて移動する
				uom.transform.DOMove(destination, UnitCst.WalkDuration).OnComplete(() =>
				{
					// 移動後の処理
					IsActing = false;

					// 移動終了、仕掛けがあれば起動する
					if(IsToActivateMcn)
					{
						MachineMng.Activate(this);
					}

					walkFinished.OnNext(Unit.Default);
					walkFinished.OnCompleted();
				});

				// プレイヤーならカメラアンカーも動かす
				if(IsPlayer())
				{
					PlayerMng.MoveCameraAnchor(MapPos, destination, UnitCst.WalkDuration);
				}
			}
			else
			{
				// 非表示ならアニメーションを行わない
				uom.gameObject.transform.position = destination;
				
				IsActing = false;

				// 移動終了、仕掛けがあれば起動する
				if(IsToActivateMcn)
				{
					MachineMng.Activate(this);
				}

				walkFinished.OnNext(Unit.Default);
				walkFinished.OnCompleted();
			}
		}


		/// <summary>
		/// このターンに仕掛けを起動するかどうか
		/// </summary>
		private bool IsToActivateMachine()
		{
			// プレイヤーでないなら仕掛けを起動しない
			if(!IsPlayer())
			{
				return false;
			}

			// 移動先に仕掛けが存在するかを調べる
			return FieldMng.FieldBo.MachineExists(MapPos);
		}


		/// <summary>
		/// 通常攻撃する
		/// </summary>
		public void Attack((int, int) targetPos = default, string stateName = default)
		{
			alreadyActed = true;

			// 装備中の武器を取得する
			var weapons = GetMeleeWeapons();

			// 装備していないなら素手を武器とする
			if(weapons.Count == 0)
			{
				weapons.Add((new EquipPartBo(EquipPart.Hand), BareHands));
			}

			// 攻撃する武器の順番を求める
			var atkWeaponOrder = CalcAtkWeaponOrder(weapons);

			// 攻撃準備情報を取得する
			var atkPrepareBo = GetAtkPrepareBo(atkWeaponOrder, targetPos);

			if(atkPrepareBo.Waves.Count == 0)
			{
				if(FieldMng.FieldBo.IsPosInMapRange(atkPrepareBo.FrontPos))
				{
					var frontItem = FieldMng.FieldBo.GetTheFrontItem(atkPrepareBo.FrontPos);

					if(frontItem == null)
					{
						// 何もないなら
						// 耕作を試みる
						if(TryCultivating(atkPrepareBo.FrontPos, weapons))
						{
							return;
						}
					}
					else
					{
						// 採掘、伐採を試みる
						if(TryMining(frontItem, weapons) || TryLogging(frontItem, weapons))
						{
							return;
						}
					}
				}

				// 攻撃アニメーション終了後の処理
				var animFinished = new Subject<Unit>();
				animFinished.Subscribe(_ =>
				{
					StartEndingTurn();
				});

				// 攻撃アニメーションを再生する
				PlayAtkAnim(stateName, animFinished, atkWeaponOrder);
			}
			else
			{
				// 攻撃アニメーション
				PlayAtkAnim(stateName, null, atkWeaponOrder);

				// 各ウェーブ毎に戦闘結果をまとめる
				for(int i = 0; i < atkPrepareBo.Waves.Count; i++)
				{
					foreach(var apgb in atkPrepareBo.Waves[i])
					{
						remainedAtkPrcsNum++;

						// 1回攻撃する
						AttackUnit(apgb.Weapon, apgb.Target);
					}
				}
			}
		}
		
		/// <summary>
		/// ユニットを1回攻撃する
		/// </summary>
		public void AttackUnit(ItemBo weapon, UnitBo target)
		{
			// 被攻撃者が既に死んでいるなら攻撃しない
			if(target.isDead)
			{
				return;
			}

			// 通常攻撃したログ
			var txt = BattleMng.GetNormalAttackTxt(weapon.WeaponBn.NormalAttackTxtId);
			var parameters = new List<object>() { target.GetName() };
			BattleMng.SubscribeLog(SysTxtMng.Replace(txt, this, parameters));

			// 攻撃が受け流されるか
			bool isFendOff = false;

			if(IsHit(weapon, target, out isFendOff))
			{
				if(BattleMng.IsParried(target))
				{
					// パリィのログ
					BattleMng.SubscribeLog(
						SysTxtMng.ReplaceSubject(BattleMng.GetMiscTxt(BattleMiscTxt.MissAtk), this));

					BattleMng.SubscribeMainProcess(() =>
					{
						// SE
						SoundMng.PlaySe(target, GeneralUtil.Random(UnitCst.ParrySeArray));

						// TODO:VFX

						// TODO:パリィアニメーション

						// メイン処理終了通知
						BattleMng.OnMainFinished();
					});

					animEvt.SubscribeOnHit(() =>
					{
						if(AtkPrcsCompleted())
						{
							// 戦闘処理スレッドを開始する
							BattleMng.Fire();
						}
					});
				}
				else
				{
					// ダメージ情報を求める
					var atkBo = BattleMng.CalcAtkBo(this, weapon);
					var dmgBo = BattleMng.CalcDmgBo(atkBo, target, isFendOff, DmgType.Melee);
					dmgBo.hitDir = target.GetHitDir(Dir);

					// ダメージを受け、被弾アニメーションを行う
					target.TakeDmgWithAnim(dmgBo);
					
					animEvt.SubscribeOnHit(() =>
					{
						// SE
						SoundMng.PlaySe(dmgBo.Target, dmgBo.Weapon.WeaponBn.RandomHitSe());

						// TODO:VFX

						if(AtkPrcsCompleted())
						{
							// 戦闘処理スレッドを開始する
							BattleMng.Fire();
						}
					});
				}
			}
			else
			{
				// 外れログ
				BattleMng.SubscribeLog(
					SysTxtMng.ReplaceSubject(BattleMng.GetMiscTxt(BattleMiscTxt.MissAtk), this));

				animEvt.SubscribeOnHit(() =>
				{
					if(AtkPrcsCompleted())
					{
						// 戦闘処理スレッドを開始する
						BattleMng.Fire();
					}
				});
			}
		}
		
		
		/// <summary>
		/// 攻撃処理が完了したことを通知する
		/// </summary>
		/// <returns>true: 全ての攻撃処理が完了した</returns>
		private bool AtkPrcsCompleted()
		{
			remainedAtkPrcsNum--;
			return remainedAtkPrcsNum == 0;
		}


		/// <summary>
		/// 攻撃アニメーションを再生する
		/// </summary>
		public void PlayAtkAnim(string stateName, Subject<Unit> animFinished, params ItemBo[] atkWeaponOrder)
		{
			foreach(var weapon in atkWeaponOrder)
			{
				// 武器を振った時の処理をアニメイベントに登録
				animEvt.SubscribeOnSwing(() =>
				{
					// 武器を振るSEを再生する
					SoundMng.PlaySe(this, weapon.WeaponBn.RandomSwingSe());
				});
			}

			// アニメーションが指定されているならそれを再生する
			if(stateName != default)
			{
				PlayAnimation(stateName, animFinished);

				return;
			}

			if(!IsHuman() && !IsSkeleton())
			{
				// 人、骸骨以外ならランダムアニメーション
				PlayAnimTrigger(AnimationMng.RandomParameterName(AnimatorCompo, AnimState.Attack));

				return;
			}

			var animPrmtNames = new string[atkWeaponOrder.Length];

			// 順番に武器ごとの攻撃アニメーションを再生する
			for(int i = 0; i < atkWeaponOrder.Length; i++)
			{
				animPrmtNames[i] = AnimationCst.PrmtHumanoidAtkBase;

				// 装備から部位を取得する
				var part = GetPartFromEquipment(atkWeaponOrder[i]);

				// 素手の場合
				if(part == null)
				{
					animPrmtNames[i] += BareHands.WeaponBn.AtkAnim1 + AnimationCst.PrmtHumanoidSuffixR;
					continue;
				}
				else
				{
					if(Is2Handed())
					{
						// 両手持ちの場合はここで処理終了
						animPrmtNames[i] += atkWeaponOrder[i].WeaponBn.AtkAnim2;
						break;
					}
					else
					{
						animPrmtNames[i] += atkWeaponOrder[i].WeaponBn.AtkAnim1;
					}
				}

				switch(part.partOrder)
				{
					case PartOrder.First:
						animPrmtNames[i] += AnimationCst.PrmtHumanoidSuffixR;
						break;

					case PartOrder.Second:
						animPrmtNames[i] += AnimationCst.PrmtHumanoidSuffixL;
						break;
				}
			}

			// 再生したアニメーション数
			int animPlayed = 0;

			// アニメーション終了時に次の攻撃アニメーションに移る
			Play();

			/// <summary>
			/// アニメーションを再生する
			/// </summary>
			void Play()
			{
				animPlayed++;
				Subject<Unit> onFinish = null;

				// アニメーション終了時の処理を登録する
				if(animPlayed != animPrmtNames.Length)
				{
					// 次のアニメーションを再生する
					onFinish = new Subject<Unit>();
					onFinish.Subscribe(_ =>
					{
						Play();
					});
				}
				else
				{
					// 最後のアニメーションなら渡された処理を行う
					onFinish = animFinished;
				}

				PlayAnimTrigger(animPrmtNames[animPlayed - 1], onFinish);
			}
		}


		/// <summary>
		/// 攻撃する武器の順番を求める
		/// </summary>
		/// <returns>index: 攻撃の順番、element: 使用武器</returns>
		private ItemBo[] CalcAtkWeaponOrder(List<(EquipPartBo, ItemBo)> weapons)
		{
			int atkNum = 0;
			var weaponAtkNumList = new List<(ItemBo, int)>(weapons.Count);

			for(int i = 0; i < weapons.Count; i++)
			{
				atkNum += weapons[i].Item2.WeaponBn.NumOfAttack;
				weaponAtkNumList.Add((weapons[i].Item2, weapons[i].Item2.WeaponBn.NumOfAttack));
			}

			// index: ヒット処理の順番、element: 使用武器
			var atkWeaponOrder = new ItemBo[atkNum];
			int cnt = 0;

			for(int i = 0; i < atkNum; i++)
			{
				atkWeaponOrder[i] = weaponAtkNumList[cnt].Item1;
				weaponAtkNumList[cnt] = (weaponAtkNumList[cnt].Item1, weaponAtkNumList[cnt].Item2 - 1);

				if(weaponAtkNumList[cnt].Item2 < 1)
				{
					weaponAtkNumList.RemoveAt(cnt);
				}
				else
				{
					cnt++;
				}

				cnt = cnt < weaponAtkNumList.Count ? cnt : 0;
			}

			return atkWeaponOrder;
		}


		/// <summary>
		/// 攻撃準備情報を取得する
		/// </summary>
		private AtkPrepareBo GetAtkPrepareBo(ItemBo[] atkWeaponOrder, (int, int) targetPos = default)
		{
			if(targetPos == default)
			{
				// 対象座標が指定されていない場合は目前の座標を対象にする
				targetPos = FieldMng.FieldBo.GetNextPos(MapPos, Dir);
			}

			var result = new AtkPrepareBo(targetPos);

			foreach(var wave in atkWeaponOrder)
			{
				var waveInfo = new List<AtkPrepareGridBo>(3);

				// 武器の攻撃範囲
				var posList = new List<(int, int)>();
				posList.Add(targetPos);

				foreach(var pos in posList)
				{
					// 障害物を取得する
					var targetUnit = FieldMng.FieldBo.GetUnit(pos);

					// 対象がない、味方・中立ユニットなら処理しない
					if(targetUnit == null || !Ai.IsFavorType((UnitBo) targetUnit, FavorType.Hostile))
					{
						continue;
					}

					waveInfo.Add(new AtkPrepareGridBo(pos, targetUnit, wave));
				}

				if(waveInfo.Count > 0)
				{
					result.AddWave(waveInfo);
				}
			}

			return result;
		}


		/// <summary>
		/// どの方向から攻撃を受けたかを求める
		/// </summary>
		/// <param name="attackDir">攻撃側の方角</param>
		/// <param name="attackPos">攻撃側の座標</param>
		public UnitDirection GetHitDir(Direction attackDir, int[] attackPos = null)
		{
			UnitDirection unitDir = UnitDirection.None;

			// 方角なし、座標が等しいなら方向なし
			if((attackDir == Direction.None && Dir == Direction.None)
				|| GeneralUtil.IsEqual(attackPos, MapPos))
			{
				return unitDir;
			}

			// 方角に割り当てられた数値の差
			int dirDiff = Dir - attackDir;

			// 方角に割り当てられた数値の中点
			int dirMidPoint = (int)Direction.UpLeft / 2;

			if(dirDiff == 0)
			{
				// 後
				unitDir = UnitDirection.Back;
			}
			else if(Mathf.Abs(dirDiff) == dirMidPoint)
			{
				// 前
				unitDir = UnitDirection.Front;
			}
			else
			{
				for(int i = 1; i <= dirMidPoint - 1; i++)
				{
					var dir1 = (int)(Dir) + i;

					if(dir1 > (int)Direction.UpLeft)
					{
						dir1 -= (int)Direction.UpLeft;
					}

					if(attackDir == (Direction)dir1)
					{
						if(i == 1)
						{
							// 右前
							unitDir = UnitDirection.RightFront;
						}
						else if(i == 2)
						{
							// 右
							unitDir = UnitDirection.Right;
						}
						else if(i == 3)
						{
							// 右後
							unitDir = UnitDirection.RightBack;
						}

						break;
					}

					var dir2 = (int)(Dir) - i;

					if(dir2 < (int)Direction.Up)
					{
						dir2 += (int)Direction.UpLeft;
					}

					if(attackDir == (Direction)dir2)
					{
						if(i == 1)
						{
							// 左前
							unitDir = UnitDirection.LeftFront;
						}
						else if(i == 2)
						{
							// 左
							unitDir = UnitDirection.Left;
						}
						else if(i == 3)
						{
							// 左後
							unitDir = UnitDirection.LeftBack;
						}

						break;
					}
				}
			}

			return unitDir;
		}


		/// <summary>
		/// 攻撃が命中したかどうか
		/// </summary>
		/// <param name="weapon">攻撃武器</param>
		/// <param name="target">被攻撃者</param>
		/// <param name="isFendOff">攻撃が当たった場合受け流されたかどうか</param>
		/// <returns>true: 命中</returns>
		private bool IsHit(ItemBo weapon, UnitBo target, out bool isFendOff)
		{
			return IsHit(CalcAccuracyValue(weapon.WeaponBn), target, out isFendOff);
		}


		/// <summary>
		/// 攻撃が命中したかどうか
		/// </summary>
		/// <param name="accuracyVal">命中値</param>
		/// <param name="target">被攻撃者</param>
		/// <param name="isFendOff">攻撃が当たった場合受け流されたかどうか</param>
		/// <returns>true: 命中</returns>
		private bool IsHit(int accuracyVal, UnitBo target, out bool isFendOff)
		{
			// 被攻撃者の回避値を求める
			int avoidanceVal = target.CalcAvoidanceValue();

			// 命中率を求める
			int hitOdds = GeneralUtil.Clamp(
				accuracyVal - avoidanceVal, BattleCst.MinAccuracy, BattleCst.MaxAccuracy);

			// 命中したかを決める
			var rand = UnityEngine.Random.Range(0, BattleCst.AccuracyParam);
			var isHit = rand < hitOdds;

			if(isHit)
			{
				// 受け流し値を求める
				var fendOffVal = CalcFendOffVal() - (hitOdds - rand);

				isFendOff = GeneralUtil.Lottery(fendOffVal, BattleCst.AccuracyParam);
			}
			else
			{
				isFendOff = false;
			}

			return isHit;
		}


		/// <summary>
		/// 攻撃の命中値を求める
		/// </summary>
		public int CalcAccuracyValue(WeaponBean weapon)
		{
			return UnitBn.Accuracy + weapon.Accuracy;
		}


		/// <summary>
		/// ユニットの回避値を求める
		/// </summary>
		public int CalcAvoidanceValue()
		{
			int avoidanceVal = ArmorBn.Avoidance;

			// 装備品から防具を取り出していく
			foreach(var pair in equipmentDict)
			{
				// 防具なら回避補正値を足す
				if(pair.Value.ItemBn.ArmorFlg)
				{
					avoidanceVal += pair.Value.ArmorBn.Avoidance;
				}
			}

			return avoidanceVal;
		}


		/// <summary>
		/// ヒットアニメーションを再生する
		/// </summary>
		/// <param name="dir">攻撃を受けた方向</param>
		protected void PlayHitAnimation(UnitDirection dir, Subject<Unit> animationFinished)
		{
			// 既にアニメーション再生中なら新たに再生しない
			if(IsActing)
			{
				animationFinished.OnNext(Unit.Default);
				animationFinished.OnCompleted();

				return;
			}

			if(shakeOnHit)
			{
				// 振動
				Shake(animationFinished);

				return;
			}

			string stateName = default;

			// 攻撃を受けた方向で再生するアニメーションを決める
			switch(dir)
			{
				case UnitDirection.LeftFront:
				case UnitDirection.Left:
				case UnitDirection.LeftBack:
					stateName = AnimationCst.StateHitLeft;
					break;

				case UnitDirection.RightFront:
				case UnitDirection.Right:
				case UnitDirection.RightBack:
					stateName = AnimationCst.StateHitRight;
					break;

				case UnitDirection.Back:
					stateName = AnimationCst.StateHitBack;
					break;
			}

			if(stateName != default && AnimatorCompo.HasState(0, Animator.StringToHash(stateName)))
			{
				// ヒットアニメーションを再生する
				PlayAnimation(stateName, animationFinished);
			}
			else
			{
				// 前からの攻撃か、該当アニメーションがないならランダムヒットアニメーションを再生する
				PlayAnimation(AnimState.Hit, animationFinished);
			}
		}



		/// <summary>
		/// アニメーションさせる
		/// </summary>
		/// <param name="animationState">アニメーションの種類</param>
		/// <param name="animFinished">アニメーション終了通知</param>
		protected void PlayAnimation(AnimState animationState, Subject<Unit> animFinished = null)
		{
			var stateName = AnimationMng.RandomNormalStateName(AnimatorCompo, animationState);
			PlayAnimation(stateName, animFinished);
		}


		/// <summary>
		/// アニメーションさせる
		/// </summary>
		/// <param name="stateName">ステート名</param>
		/// <param name="animFinished">アニメーション終了通知</param>
		public void PlayAnimation(string stateName, Subject<Unit> animFinished = null)
		{
			MonoDelegator.Instance.StartCoroutine(Main());

			/// <summary>
			/// 実処理
			/// </summary>
			IEnumerator Main()
			{
				IsActing = true;

				// 1フレーム待たないとヒットアニメーションとの整合性が合わないことがある
				yield return null;

				if(stateName != "")
				{
					// アニメーションを開始する
					AnimatorCompo.Play(stateName, 0);

					// 次フレームでないとステートが切り替わらない
					yield return null;

					// アニメーションの秒数だけ待つ
					var sec = AnimationMng.GetCurAnimationLength(AnimatorCompo);
					yield return new WaitForSeconds(sec);
				}

				IsActing = false;

				// アニメーション終了を通知する
				if(animFinished != null)
				{
					animFinished.OnNext(Unit.Default);
					animFinished.OnCompleted();
				}
			}
		}


		/// <summary>
		/// Boolでアニメーションさせる
		/// </summary>
		public void PlayAnimBool(string parameterName, bool val, Subject<Unit> animFinished = null)
		{
			// 終了時の処理を登録する
			animEvt.SubscribeOnFinish(() =>
			{
				IsActing = false;

				// アニメーション終了を通知する
				if(animFinished != null)
				{
					animFinished.OnNext(Unit.Default);
					animFinished.OnCompleted();
				}
			});

			MonoDelegator.Instance.StartCoroutine(Main());

			/// <summary>
			/// 実処理
			/// </summary>
			IEnumerator Main()
			{
				IsActing = true;

				// 1フレーム待たないとヒットアニメーションとの整合性が合わないことがある
				yield return null;

				// アニメーションを開始する
				AnimatorCompo.SetBool(parameterName, val);
			}
		}


		/// <summary>
		/// Triggerでアニメーションさせる
		/// </summary>
		public void PlayAnimTrigger(string parameterName, Subject<Unit> animFinished = null)
		{
			// 終了時の処理を登録する
			animEvt.SubscribeOnFinish(() =>
			{
				IsActing = false;

				// アニメーション終了を通知する
				if(animFinished != null)
				{
					animFinished.OnNext(Unit.Default);
					animFinished.OnCompleted();
				}
			});

			MonoDelegator.Instance.StartCoroutine(Main());

			/// <summary>
			/// 実処理
			/// </summary>
			IEnumerator Main()
			{
				IsActing = true;

				// 1フレーム待たないとヒットアニメーションとの整合性が合わないことがある
				yield return null;

				// アニメーションを開始する
				AnimatorCompo.SetTrigger(parameterName);
			}
		}


		/// <summary>
		/// ヒット時処理を登録してアニメーションを再生する
		/// </summary>
		public void PlayAnimTriggerWithOnHit(string triggerName, Action onHit)
		{
			animEvt.SubscribeOnHit(onHit);
			PlayAnimTrigger(triggerName);
		}


		/// <summary>
		/// オブジェクトの方を向く
		/// </summary>
		public void Rotate(AObject obj, bool isSlowly, Subject<Unit> animFinished = null)
		{
			Rotate(FieldMng.GetDirection(MapPos, obj.MapPos), isSlowly, animFinished);
		}


		/// <summary>
		/// 方向転換する
		/// </summary>
		/// <param name="isSlowly">
		/// 回転にRotateSlowly()を使うかどうか
		/// </param>
		public void Rotate(Direction dir, bool isSlowly, Subject<Unit> animFinished = null)
		{
			Dir = dir;

			var eulerAngles = PfbBean.DefRotate.eulerAngles;

			// 渡された方向で回転する角度を決める
			switch(dir)
			{
				case Direction.DownLeft:
					eulerAngles.y += UnitCst.AngleDownLeft;
					break;

				case Direction.Down:
					eulerAngles.y += UnitCst.AngleDown;
					break;

				case Direction.DownRight:
					eulerAngles.y += UnitCst.AngleDownRight;
					break;

				case Direction.Left:
					eulerAngles.y += UnitCst.AngleLeft;
					break;

				case Direction.Right:
					eulerAngles.y += UnitCst.AngleRight;
					break;

				case Direction.UpLeft:
					eulerAngles.y += UnitCst.AngleUpLeft;
					break;

				case Direction.Up:
					eulerAngles.y += UnitCst.AngleUp;
					break;

				case Direction.UpRight:
					eulerAngles.y += UnitCst.AngleUpRight;
					break;
			}
			
			if(isSlowly && Visible)
			{
				MonoDelegator.Instance.StartCoroutine(RotateSlowly(eulerAngles.y, animFinished));
			}
			else
			{
				uom.transform.rotation = Quaternion.Euler(eulerAngles.x, eulerAngles.y, eulerAngles.z);

				if(animFinished != null)
				{
					animFinished.OnNext(Unit.Default);
					animFinished.OnCompleted();
				}
			}
		}


		/// <summary>
		/// ゆっくり方向転換する
		/// </summary>
		private IEnumerator RotateSlowly(float angleY, Subject<Unit> animFinished)
		{
			IsActing = true;

			var tf = uom.transform;
			var curAngleY = tf.eulerAngles.y;
			var transAngle = (float) Math.Round(angleY - curAngleY, 1);

			// 目的の角度にたどり着くのに最短の回転で済む角度を求める
			if(transAngle > 180)
			{
				transAngle = (float) Math.Round(transAngle - 360);
			}
			else if(transAngle < -180)
			{
				transAngle = (float) Math.Round(transAngle + 360);
			}

			for(int num = 0; num < GameCst.AnimationFluidity; num++)
			{
				var eulerAngles = tf.eulerAngles;
				tf.rotation = Quaternion.Euler(eulerAngles.x,
					curAngleY + (transAngle / GameCst.AnimationFluidity), eulerAngles.z);
				curAngleY = tf.eulerAngles.y;

				var waitSec = GameCst.RotateDuration / GameCst.AnimationFluidity / aniSpd;
				yield return new WaitForSeconds(waitSec);
			}

			IsActing = false;

			if(animFinished != null)
			{
				animFinished.OnNext(Unit.Default);
				animFinished.OnCompleted();
			}
		}


		/// <summary>
		/// アイテムを食べる
		/// </summary>
		/// <param name="food">食べるアイテム</param>
		public void Eat(ItemBo food)
		{
			// 満腹度を増加させる
			IncreaseSatiety(food.GetSatiety());
			IncreaseWater(food.GetWater());

			// 食べ物のスタック数を一つ減らす
			food.DecreaseStack();

			// ログ
			var param = new List<object>() { food.GetName() };
			MsgBoardMng.Log(SysTxtMng.GetTxt(SysTxtId.Eat, this, param));

			// 食事SEをランダムに一つ選んで再生する
			var seIdList = new List<SeId>() { SeId.Eat1, SeId.Eat2, SeId.Eat3 };
			SoundMng.PlaySe(this, GeneralUtil.Random(seIdList));

			// ターン終了
			StartEndingTurn();
		}


		/// <summary>
		/// 瓶に入った液体を飲む
		/// </summary>
		/// <param name="bottle">瓶</param>
		public void Drink(ItemBo bottle)
		{
			var bottleMdl = bottle.GetModule<BottleMdl>();

			// 液体を減らす
			bottleMdl.RemoveOne(this);

			// 飲む処理の共通部分
			DrinkCommonProcess(bottleMdl.Liquid);
		}


		/// <summary>
		/// 液体を飲む
		/// </summary>
		public void Drink(string liquidObjId)
		{
			// 瓶なしの液体を生成する
			var liquid = new ItemBo(ItemMng.GetItemMst(liquidObjId), 1);

			// 飲む処理の共通部分
			DrinkCommonProcess(liquid);
		}


		/// <summary>
		/// 飲む処理の共通部分
		/// </summary>
		private void DrinkCommonProcess(ItemBo liquid)
		{
			// 満腹度を増加させる
			IncreaseSatiety(liquid.GetSatiety());
			IncreaseWater(liquid.GetWater());

			// ログ
			var param = new List<object>() { liquid.GetName() };
			MsgBoardMng.Log(SysTxtMng.GetTxt(SysTxtId.Drink, this, param));

			// 飲むSEをランダムに一つ選んで再生する
			var seIdList = new List<SeId>() { SeId.Drink1, SeId.Drink2, SeId.Drink3 };
			SoundMng.PlaySe(this, GeneralUtil.Random(seIdList));

			// ターン終了
			StartEndingTurn();
		}


		/// <summary>
		/// 満腹度を増加させる
		/// </summary>
		public void IncreaseSatiety(int byVal)
		{
			if(byVal < 0)
			{
				return;
			}

			// 満腹度を足す
			UnitBn.satiety = GeneralUtil.Addition(UnitBn.satiety, byVal);

			// 満腹状態を計算する
			if(IsPlayer() && CalcSatietyStatus())
			{
				// 満腹度変化ログ
				SysTxtId id = default;

				switch(GetSatietyStatus())
				{
					case StatusEffect.Overeating:
						id = SysTxtId.SatietyPlus1;
						break;

					case StatusEffect.Full:
						id = SysTxtId.SatietyPlus2;
						break;

					case StatusEffect.Dummy:
						id = SysTxtId.SatietyPlus3;
						break;

					case StatusEffect.Hungry:
						id = SysTxtId.SatietyPlus4;
						break;

					case StatusEffect.Ravenous:
						id = SysTxtId.SatietyPlus5;
						break;
				}

				MsgBoardMng.Log(SysTxtMng.GetTxt(id));
			}
		}


		/// <summary>
		/// 満腹度を消費する
		/// </summary>
		/// <param name="defSatietyCost">標準満腹度コスト</param>
		protected void ConsumeSatiety(int defSatietyCost)
		{
			if(defSatietyCost < 0)
			{
				return;
			}

			// このキャラのコストを求める
			int cost = (int) (defSatietyCost * UnitBn.satietyCostCoeff);

			// 最小値を超えないように満腹度を足す
			UnitBn.satiety = Mathf.Max(0, GeneralUtil.Subtraction(UnitBn.satiety, cost));

			// 満腹状態を計算する
			if(IsPlayer() && CalcSatietyStatus())
			{
				// 満腹度変化ログ
				SysTxtId id = default;

				switch(GetSatietyStatus())
				{
					case StatusEffect.Hungry:
						id = SysTxtId.SatietyMinus1;
						break;

					case StatusEffect.Ravenous:
						id = SysTxtId.SatietyMinus2;
						break;

					case StatusEffect.Starving:
						id = SysTxtId.SatietyMinus3;
						break;

					default:
						return;
				}

				MsgBoardMng.Log(SysTxtMng.GetTxt(id));
			}
		}


		/// <summary>
		/// 現在の満腹状態を取得する
		/// </summary>
		public StatusEffect GetSatietyStatus()
		{
			foreach(var seBo in statusEffectBoList)
			{
				if(seBo.StatusEfct == StatusEffect.Overeating || seBo.StatusEfct == StatusEffect.Full
					|| seBo.StatusEfct == StatusEffect.Hungry || seBo.StatusEfct == StatusEffect.Ravenous
					|| seBo.StatusEfct == StatusEffect.Starving)
				{
					return seBo.StatusEfct;
				}
			}

			return StatusEffect.Dummy;
		}


		/// <summary>
		/// 満腹状態を計算する
		/// </summary>
		private bool CalcSatietyStatus()
		{
			// 満腹度に従って状態異常を決定する
			if(UnitBn.satiety > UnitBn.maxSatiety)
			{
				// 食べ過ぎ
				return ChangeSatietyStatus(StatusEffect.Overeating);
			}
			else if(UnitBn.satiety > (int) (UnitBn.maxSatiety * UnitCst.SatFullRation))
			{
				// 満腹
				return ChangeSatietyStatus(StatusEffect.Full);
			}
			else if(UnitBn.satiety > (int) (UnitBn.maxSatiety * UnitCst.SatNormalRation))
			{
				// 普通
				return RemoveSatietyStatus();
			}
			else if(UnitBn.satiety > (int) (UnitBn.maxSatiety * UnitCst.SatHungryRation))
			{
				// 空腹
				return ChangeSatietyStatus(StatusEffect.Hungry);
			}
			else if(UnitBn.satiety > 0)
			{
				// 激しい空腹
				return ChangeSatietyStatus(StatusEffect.Ravenous);
			}
			else
			{
				// 飢餓
				return ChangeSatietyStatus(StatusEffect.Starving);
			}

			/// <summary>
			/// 満腹状態を変更する
			/// </summary>
			bool ChangeSatietyStatus(StatusEffect newSatietyStatus)
			{
				// 現在の満腹状態と同じなら処理しない
				if(HasStatusEffect(newSatietyStatus))
				{
					return false;
				}

				// 既存の満腹状態を消去する
				RemoveSatietyStatus();

				// 新しい満腹状態を追加する
				AddStatusEffect(newSatietyStatus, true);

				return true;
			}

			/// <summary>
			/// 満腹状態を削除する
			/// </summary>
			bool RemoveSatietyStatus()
			{
				if(RemoveStatusEffect(StatusEffect.Overeating))
				{
					return true;
				}

				if(RemoveStatusEffect(StatusEffect.Full))
				{
					return true;
				}

				if(RemoveStatusEffect(StatusEffect.Hungry))
				{
					return true;
				}

				if(RemoveStatusEffect(StatusEffect.Ravenous))
				{
					return true;
				}

				return RemoveStatusEffect(StatusEffect.Starving);
			}
		}


		/// <summary>
		/// 潤い値を増加させる
		/// </summary>
		public void IncreaseWater(int byVal)
		{
			if(byVal < 0)
			{
				return;
			}

			// 潤い値を足す
			UnitBn.water = GeneralUtil.Addition(UnitBn.water, byVal);
			
			// 潤い状態を計算する
			if(IsPlayer() && CalcWaterStatus())
			{
				// 潤い変化ログ
				SysTxtId id = default;

				switch(GetWaterStatus())
				{
					case StatusEffect.Overdrinking:
						id = SysTxtId.WaterPlus1;
						break;

					case StatusEffect.Moisture:
						id = SysTxtId.WaterPlus2;
						break;

					case StatusEffect.Dummy:
						id = SysTxtId.WaterPlus3;
						break;

					case StatusEffect.Thirsty:
						id = SysTxtId.WaterPlus4;
						break;

					case StatusEffect.Withered:
						id = SysTxtId.WaterPlus5;
						break;
				}

				MsgBoardMng.Log(SysTxtMng.GetTxt(id), false);
			}
		}


		/// <summary>
		/// 潤い値を消費する
		/// </summary>
		/// <param name="defWaterCost">標準潤い値コスト</param>
		protected void ConsumeWater(int defWaterCost)
		{
			if(defWaterCost < 0)
			{
				return;
			}

			// このキャラのコストを求める
			int cost = (int) (defWaterCost * UnitBn.waterCostCoeff);

			// 最小値を超えないように潤い値を足す
			UnitBn.water = Mathf.Max(0, GeneralUtil.Subtraction(UnitBn.water, cost));

			// 潤い状態を計算する
			if(CalcWaterStatus() && IsPlayer())
			{
				// 潤い値変化ログ
				SysTxtId id = default;

				switch(GetWaterStatus())
				{
					case StatusEffect.Thirsty:
						id = SysTxtId.WaterMinus1;
						break;

					case StatusEffect.Withered:
						id = SysTxtId.WaterMinus2;
						break;

					case StatusEffect.Sapless:
						id = SysTxtId.WaterMinus3;
						break;

					default:
						return;
				}

				MsgBoardMng.Log(SysTxtMng.GetTxt(id));
			}
		}


		/// <summary>
		/// 現在の潤い状態を取得する
		/// </summary>
		public StatusEffect GetWaterStatus()
		{
			foreach(var seBo in statusEffectBoList)
			{
				if(seBo.StatusEfct == StatusEffect.Overdrinking || seBo.StatusEfct == StatusEffect.Moisture
					|| seBo.StatusEfct == StatusEffect.Thirsty || seBo.StatusEfct == StatusEffect.Withered
					|| seBo.StatusEfct == StatusEffect.Sapless)
				{
					return seBo.StatusEfct;
				}
			}

			return StatusEffect.Dummy;
		}


		/// <summary>
		/// 潤い状態を計算する
		/// </summary>
		private bool CalcWaterStatus()
		{
			// 潤い値に従って状態異常を決定する
			if(UnitBn.water > UnitBn.maxWater)
			{
				// 飲み過ぎ
				return ChangeWaterStatus(StatusEffect.Overdrinking);
			}
			else if(UnitBn.water > (int) (UnitBn.maxWater * UnitCst.WaterMoistureRation))
			{
				// 潤い
				return ChangeWaterStatus(StatusEffect.Moisture);
			}
			else if(UnitBn.water > (int) (UnitBn.maxWater * UnitCst.WaterNormalRation))
			{
				// 普通
				return RemoveWaterStatus();
			}
			else if(UnitBn.water > (int) (UnitBn.maxWater * UnitCst.WaterThirstyRation))
			{
				// 渇き
				return ChangeWaterStatus(StatusEffect.Thirsty);
			}
			else if(UnitBn.water > 0)
			{
				// 激しい渇き
				return ChangeWaterStatus(StatusEffect.Withered);
			}
			else
			{
				// 干からび
				return ChangeWaterStatus(StatusEffect.Sapless);
			}

			/// <summary>
			/// 潤い状態を変更する
			/// </summary>
			bool ChangeWaterStatus(StatusEffect newWaterStatus)
			{
				// 現在の潤い状態と同じなら処理しない
				if(HasStatusEffect(newWaterStatus))
				{
					return false;
				}

				// 既存の潤い状態を消去する
				RemoveWaterStatus();

				// 新しい潤い状態を追加する
				AddStatusEffect(newWaterStatus, true);

				return true;
			}

			/// <summary>
			/// 潤い状態を削除する
			/// </summary>
			bool RemoveWaterStatus()
			{
				if(RemoveStatusEffect(StatusEffect.Overdrinking))
				{
					return true;
				}

				if(RemoveStatusEffect(StatusEffect.Moisture))
				{
					return true;
				}

				if(RemoveStatusEffect(StatusEffect.Thirsty))
				{
					return true;
				}

				if(RemoveStatusEffect(StatusEffect.Withered))
				{
					return true;
				}

				return RemoveStatusEffect(StatusEffect.Sapless);
			}
		}


		/// <summary>
		/// 指定する状態異常の数を取得する
		/// </summary>
		public int GetStatusEffectNum(StatusEffect statusEffect)
		{
			int count = 0;

			foreach(var seBo in statusEffectBoList)
			{
				if(seBo.StatusEfct == statusEffect)
				{
					count++;
				}
			}

			return count;
		}


		/// <summary>
		/// 状態異常情報を取得する
		/// </summary>
		/// <returns>なければnull</returns>
		private StatusEffectBo GetStatusEffectBo(StatusEffect statusEffect)
		{
			foreach(var seBo in statusEffectBoList)
			{
				if(seBo.StatusEfct == statusEffect)
				{
					return seBo;
				}
			}

			return null;
		}


		/// <summary>
		/// 指定の状態異常を持っているかを調べる
		/// </summary>
		public bool HasStatusEffect(StatusEffect statusEffect)
		{
			return GetStatusEffectBo(statusEffect) != null;
		}


		/// <summary>
		/// 状態異常を追加する
		/// </summary>
		public void AddStatusEffect(StatusEffect se, bool instantly)
		{
			// 状態異常情報を取得する
			var seb = GetStatusEffectBo(se);

			if(seb == null)
			{
				switch(se)
				{
					case StatusEffect.MentalCollapse:
						seb = new SEMentalCollapseBo(this, se, instantly);
						break;

					case StatusEffect.Paralyze:
						seb = new SEParalyzeBo(this, se, instantly);
						break;

					case StatusEffect.Slow:
						seb = new SESlowBo(this, se, instantly);
						break;

					case StatusEffect.Poison:
						seb = new SEPoisonBo(this, se, instantly);
						break;

					default:
						seb = new StatusEffectBo(this, se);
						break;
				}

				statusEffectBoList.Add(seb);
			}
			else
			{
				// 既に同じ状態異常があるなら
				switch(se)
				{
					case StatusEffect.Paralyze:
						seb.Refresh();
						break;

					case StatusEffect.Slow:
						seb = new SESlowBo(this, se, instantly);
						statusEffectBoList.Add(seb);
						break;

					default:
						return;
				}
			}

			if(IsPlayer())
			{
				// 主人公の場合はUIに表示する
				StatusEffectMng.AddStatusEffect(seb);
			}
		}


		/// <summary>
		/// 状態異常を除去する
		/// </summary>
		public bool RemoveStatusEffect(StatusEffect se)
		{
			StatusEffectBo seb = null;

			foreach(var tmpSeb in statusEffectBoList)
			{
				if(tmpSeb.StatusEfct == se)
				{
					seb = tmpSeb;
					break;
				}
			}

			if(seb == null)
			{
				return false;
			}

			return RemoveStatusEffect(seb);
		}


		/// <summary>
		/// 状態異常を除去する
		/// </summary>
		public bool RemoveStatusEffect(StatusEffectBo seb)
		{
			bool removed = statusEffectBoList.Remove(seb);

			if(IsPlayer() && !HasStatusEffect(seb.StatusEfct))
			{
				// 主人公の場合はUIから表示を削除する
				StatusEffectMng.RemoveStatusEffect(seb.StatusEfct);
			}

			return removed;
		}


		/// <summary>
		/// 全ての状態異常を除去する
		/// </summary>
		public void ClearStatusEffect()
		{
			// 状態異常リストをコピーする
			var copy = new List<StatusEffectBo>(statusEffectBoList);

			foreach(var seb in copy)
			{
				RemoveStatusEffect(seb);
			}
		}


		/// <summary>
		/// ダメージを受ける
		/// </summary>
		/// <param name="dmgBo">ダメージ情報</param>
		public void TakeDmg(DmgBo dmgBo, Subject<Unit> takeDmgFinished)
		{
			// ダメージを受ける直前の処理
			BeforeTakeDmg(dmgBo);

			// 総ダメージを取得する
			var dmg = dmgBo.GetHpDmg();

			// HPとWPをセットする
			UnitBn.SetHitP(GeneralUtil.Subtraction(UnitBn.HitP, dmg));
			UnitBn.SetWillP(GeneralUtil.Subtraction(UnitBn.WillP, dmgBo.spiritDmg), this);

			// HPが0以下になったら死亡する
			isDead = UnitBn.HitP <= 0;

			if(Visible)
			{
				// ダメージを受けたログ
				MsgBoardMng.Log(GetLogTakeDmg(dmgBo.Attacker, dmg, dmgBo.spiritDmg));
			}

			// 身体部位にダメージを受ける
			TakeBodyPartDmg(dmgBo);

			if(Visible)
			{
				if(isDead)
				{
					// 死亡ログ
					MsgBoardMng.Log(GetDieLog(dmgBo), false);
				}
			}

			AfterTakeDmg(dmgBo, false);

			// ダメージを受けた後の処理
			dmgProcessFlags.ObserveEveryValueChanged(val => val.AllFinished)
				.Where(val => true)
				.Subscribe(val =>
				{
					if(isDead && !dieProcessed)
					{
						// 死亡後の処理
						var dieFinished = new Subject<Unit>();
						dieFinished.Subscribe(_2 =>
						{
							takeDmgFinished.OnNext(Unit.Default);
							takeDmgFinished.OnCompleted();
						});

						// 死ぬ
						Die(dieFinished);
					}
					else
					{
						takeDmgFinished.OnNext(Unit.Default);
						takeDmgFinished.OnCompleted();
					}
				}).
				AddTo(uom.gameObject);  // ダメージ計算時にユニットのGameObjectを破棄する改修を加える場合は要注意
		}


		/// <summary>
		/// ダメージを受け、被弾アニメーションを行う
		/// </summary>
		/// <param name="dmgBo">ダメージ情報</param>
		/// <param name="hitDir">攻撃を受けた方向</param>
		/// <param name="hitFinished">被弾アニメーション終了後の処理</param>
		/// <returns>true: 攻撃対象が死んだ</returns>
		public bool TakeDmgWithAnim(DmgBo dmgBo)
		{
			// ダメージを受ける直前の処理
			BeforeTakeDmg(dmgBo);

			// 総ダメージを取得する
			var dmg = dmgBo.GetHpDmg();

			// HPとWPをセットする
			UnitBn.SetHitP(GeneralUtil.Subtraction(UnitBn.HitP, dmg));
			UnitBn.SetWillP(GeneralUtil.Subtraction(UnitBn.WillP, dmgBo.spiritDmg), this, false);

			// HPが0以下になったら死亡する
			isDead = UnitBn.HitP <= 0;

			if(Visible)
			{
				if(dmgBo.IsFendOff)
				{
					// かすり傷のログ
					BattleMng.SubscribeLog(BattleMng.GetMiscTxt(BattleMiscTxt.Graze));
				}
				else
				{
					// ダメージを受けたログ
					BattleMng.SubscribeLog(GetLogTakeDmg(dmgBo.Attacker, dmg, dmgBo.spiritDmg));
				}
			}

			// 身体部位にダメージを受ける
			TakeBodyPartDmg(dmgBo);

			if(Visible)
			{
				if(isDead && !dieProcessed)
				{
					// 死亡ログ
					BattleMng.SubscribeLog(GetDieLog(dmgBo));
				}

				// 出血エフェクト
				BleedWhenHit(dmgBo, false);

				BattleMng.SubscribeMainProcess(() =>
				{
					// ダメージを受けるアニメーション終了後の処理
					var hitAnimationFinished = new Subject<Unit>();
					hitAnimationFinished.Subscribe(_ =>
					{
						// メイン処理終了通知
						BattleMng.OnMainFinished();
					});

					// ダメージを受けるアニメーションを再生する
					PlayHitAnimation(dmgBo.hitDir, hitAnimationFinished);
				});
			}

			// ダメージを受けた後の処理
			AfterTakeDmg(dmgBo, true);

			// ダメージを受けた後の処理
			dmgProcessFlags.ObserveEveryValueChanged(val => val.AllFinished)
				.Where(val => true)
				.Subscribe(val =>
				{
					BattleMng.SubscribePostProcess(() =>
					{
						if(isDead && !dieProcessed)
						{
							// 死亡後の処理
							var dieFinished = new Subject<Unit>();
							dieFinished.Subscribe(_2 =>
							{
								// 後処理終了通知
								BattleMng.OnPostFinished();
							});

							// 死ぬ
							Die(dieFinished);
						}
						else
						{
							// 後処理終了通知
							BattleMng.OnPostFinished();
						}
					});
				}).
				AddTo(uom.gameObject);	// ダメージ計算時にユニットのGameObjectを破棄する改修を加える場合は要注意

			return isDead;
		}


		/// <summary>
		/// ダメージを受ける直前の処理
		/// </summary>
		private void BeforeTakeDmg(DmgBo dmgBo)
		{
			// ループ中にリストを操作することがあるため
			var copiedList = new List<ASustainSkillBo>(sustainSkills);

			// 常駐スキルの処理
			foreach(var skill in copiedList)
			{
				skill.BeforeTakeDmg(dmgBo);
			}
		}


		/// <summary>
		/// ダメージを受けた後の処理
		/// </summary>
		/// <param name="instantly">true: 即座に状態異常を付与する</param>
		private void AfterTakeDmg(DmgBo dmgBo, bool instantly)
		{
			// ユニット固有のダメージを受けた後の処理
			AfterTakeDmgModule(dmgBo);

			if(!isDead)
			{
				// 麻痺する
				if(dmgBo.paralyze)
				{
					// 状態異常を付与
					AddStatusEffect(StatusEffect.Paralyze, instantly);
				}

				// 鈍足になる
				if(dmgBo.slow)
				{
					// 状態異常を付与
					AddStatusEffect(StatusEffect.Slow, instantly);
				}

				// 毒になる
				if(dmgBo.poison)
				{
					// 状態異常を付与
					AddStatusEffect(StatusEffect.Poison, instantly);
				}
			}

			dmgProcessFlags.afterTakeDmgFinished = true;
		}


		/// <summary>
		/// 身体部位にダメージを受ける
		/// </summary>
		/// <returns>true: アニメーションを行った</returns>
		void TakeBodyPartDmg(DmgBo dmgBo)
		{
			if(dmgBo.TargetBodyPartBo == null)
			{
				dmgProcessFlags.dropWeaponAnimFinished = true;
				return;
			}

			// ダメージを受ける前の負傷度合い
			var originalInjuryDegree = dmgBo.TargetBodyPartBo.GetInjuryDegree();

			// 最大HPにおけるHPダメージの割合の倍が部位ダメージとなる
			dmgBo.TargetBodyPartBo.InjuryHp = dmgBo.TargetBodyPartBo.InjuryHp -
				(int) GeneralUtil.Round(dmgBo.GetHpDmg() / (float) UnitBn.MaxHitP * 2 * UnitCst.BpMaxHp);

			// ダメージを受けた後の負傷度合い
			var injuryDegree = dmgBo.TargetBodyPartBo.GetInjuryDegree();

			// 負傷度合いが変わったらログを出す
			if(injuryDegree != originalInjuryDegree)
			{
				BattleMiscTxt txtEnum = default;

				switch(injuryDegree)
				{
					case InjuryDegree.Minor:
						txtEnum = BattleMiscTxt.Injured1;
						break;

					case InjuryDegree.Moderate:
						txtEnum = BattleMiscTxt.Injured2;
						break;

					default:
						txtEnum = BattleMiscTxt.Injured3;
						break;
				}

				if(Visible)
				{
					BattleMng.SubscribeLog(SysTxtMng.Replace(BattleMng.GetMiscTxt(txtEnum), this,
						new List<object>() { UnitMng.BodypartsTxts[dmgBo.TargetBodyPartBo.Part] }));
				}
			}

			// 対象部位が腕でなく十分に負傷しないなら武器を落とさない
			if(injuryDegree != InjuryDegree.Severe
				|| (dmgBo.TargetBodyPartBo.Part != BodyPart.ArmR && dmgBo.TargetBodyPartBo.Part != BodyPart.ArmL))
			{
				dmgProcessFlags.dropWeaponAnimFinished = true;
				return;
			}

			// 身体部位に関連する武器の装備部位を取得する
			var equipPartBo = GetWeaponEquipPartBo(dmgBo.TargetBodyPartBo);

			// 指定部位に装備しているアイテムを取得する
			var weapon = GetEquipmentFromPart(equipPartBo);

			if(weapon == null)
			{
				dmgProcessFlags.dropWeaponAnimFinished = true;
				return;
			}

			// インベントリに加えずに武器を外す
			Unequip(equipPartBo, false);

			// 武器を落とす座標をランダムに取得する
			(int, int) dropPos = MapPos;
			var randomOrdered = GeneralUtil.RandomOrder(FieldMng.GetMapPosWithin(MapPos, 1));

			foreach(var pos in randomOrdered)
			{
				if(FieldMng.FieldBo.IsDeployable(pos, weapon.ObjBean))
				{
					dropPos = pos;
					break;
				}
			}

			// 今の武器GameObjectをキャッシュし、インスタンスとの関係を切る
			var currentWeaponTf = weapon.uom.transform;
			weapon.uom = null;

			if(!Visible || currentWeaponTf == null)
			{
				// 手にまだ持っている武器オブジェクトを更新する
				EquipmentMng.RefreshWeaponObjs(this, uom.gameObject);

				// 武器を落とすアニメーションしない場合
				// 武器をフィールドに生成する
				FieldMng.FieldBo.AddItem(weapon, dropPos);
				weapon.PlayDropSe();

				dmgProcessFlags.dropWeaponAnimFinished = true;
				return;
			}

			// 武器を落とすログ
			BattleMng.SubscribeLog(SysTxtMng.Replace(BattleMng.GetMiscTxt(BattleMiscTxt.DropWeapon), this,
				new List<object>() { weapon }));

			// 武器を落とすSE
			var seId = GeneralUtil.Random(new List<SeId>() { SeId.Swing1, SeId.Swing2 });
			SoundMng.PlaySe(currentWeaponTf.gameObject, seId);

			// 武器GameObjectが落ちるワールド座標
			var dropWorldPos = FieldMng.ToWorldCoordCenter(dropPos);

			// 武器GameObjectを手から外し、落とすアニメーションを再生する
			currentWeaponTf.SetParent(null);
			currentWeaponTf.DORotate(new Vector3(0, 0, 360f), UnitCst.DropAnimationDuration, RotateMode.FastBeyond360);
			currentWeaponTf.DOMoveX(dropWorldPos.x, UnitCst.DropAnimationDuration);
			currentWeaponTf.DOMoveY(dropWorldPos.y, UnitCst.DropAnimationDuration)
				.SetEase(GeneralMng.Script.GetComponent<AnimationCurves>().dropItem);
			currentWeaponTf.DOMoveZ(dropWorldPos.z, UnitCst.DropAnimationDuration)
				.OnCompleteAsObservable()
				.Subscribe(_ =>
				{
					// 手にまだ持っている武器オブジェクトを更新する
					EquipmentMng.RefreshWeaponObjs(this, uom.gameObject);

					// アニメーション終了後に
					// 武器をフィールドに生成する
					FieldMng.FieldBo.AddItem(weapon, dropPos);
					weapon.PlayDropSe();

					// 今の武器GameObjectを破棄する
					GameObject.Destroy(currentWeaponTf.gameObject);

					dmgProcessFlags.dropWeaponAnimFinished = true; ;
				})
				.AddTo(currentWeaponTf);
		}


		/// <summary>
		/// ダメージを受けた時の出血エフェクト
		/// </summary>
		private void BleedWhenHit(DmgBo dmgBo, bool bleedInstantly)
		{
			// 出血値 = （物理ダメージ / 総HP） * 血の出やすさ * 物理属性の出血係数 * 2
			var bleedVal = UnitBn.BleedCoeff * 2 *
				((dmgBo.slashingDmg / (double) UnitBn.MaxHitP) * BattleCst.BloodCoeffSlash
				+ (dmgBo.piercingDmg / (double) UnitBn.MaxHitP) * BattleCst.BloodCoeffPierce
				+ (dmgBo.crushingDmg / (double) UnitBn.MaxHitP) * BattleCst.BloodCoeffCrush);

			string effectName = "";

			// 出血値で生成する出血エフェクト名を決める
			if(bleedVal < 0.1)
			{
				// 出血なし
				dmgBo.bleedLvl = BleedLvl.Lvl0;
				return;
			}
			else if(bleedVal < 0.25)
			{
				// 小出血
				dmgBo.bleedLvl = BleedLvl.Lvl1;
				effectName = EffectCst.PfbBleed1;
			}
			else if(bleedVal < 0.5)
			{
				// 中出血
				dmgBo.bleedLvl = BleedLvl.Lvl2;
				effectName = EffectCst.PfbBleed2;
			}
			else if(bleedVal < 0.8)
			{
				// 大出血
				dmgBo.bleedLvl = BleedLvl.Lvl3;
				effectName = EffectCst.PfbBleed3;
			}
			else
			{
				// 致命的な出血
				dmgBo.bleedLvl = BleedLvl.Lvl4;
				effectName = EffectCst.PfbBleed4;
			}

			if(bleedInstantly)
			{
				// 出血エフェクトを生成する
				EffectMng.SpawnBleedEffect(this, effectName);
			}
			else
			{
				BattleMng.SubscribeMainProcess(() =>
				{
					// 出血エフェクトを生成する
					EffectMng.SpawnBleedEffect(this, effectName);

					// メイン処理終了通知
					BattleMng.OnMainFinished();
				});
			}
		}


		/// <summary>
		/// 死ぬ
		/// </summary>
		protected void Die(Subject<Unit> dieFinished)
		{
			if(IsSkeleton())
			{
				// 骸骨の場合
				// アニメイベントがないためここで死亡SEを出す
				SoundMng.PlaySe(uom.transform.position, UnitBn.RandomDieSeId());

				// 骸骨の死亡アニメーションを行う
				PlayDieAnimSkeleton();

				DieAnimFinished();

				return;
			}

			if(Visible)
			{
				// 死亡アニメーション終了後の処理
				var dieAnimationFinished = new Subject<Unit>();
				dieAnimationFinished.Subscribe(_1 =>
				{
					DieAnimFinished();
				});

				// 死亡アニメーションを再生する
				PlayDieAnimation(dieAnimationFinished);
			}
			else
			{
				DieAnimFinished();
			}

			/// <summary>
			/// 死亡アニメーション終了後の処理
			/// </summary>
			void DieAnimFinished()
			{
				dieProcessed = true;

				// プレイヤーなら血のオーバーレイの非活性状態にする
				if(IsPlayer())
				{
					HudMng.SwitchOverlayBlood();
				}

				// 記憶子を放出する
				ReleaseMemorino();

				// 全ての状態異常を除去する
				ClearStatusEffect();

				// フィールドから削除する
				FieldMng.FieldBo.RemoveUnit(MapPos, false);

				// 死体を残す
				BeCorpse();

				// 渡された処理を実行する
				if(dieFinished != null)
				{
					dieFinished.OnNext(Unit.Default);
					dieFinished.OnCompleted();
				}
			}
		}


		/// <summary>
		/// 骸骨の死亡アニメーションを行う
		/// </summary>
		public void PlayDieAnimSkeleton(bool stayBroken = false)
		{
			// モデルをGenericに切り替えて死亡アニメーションを行う
			foreach(var child in GeneralUtil.GetChildren(uom.gameObject))
			{
				if(child.name == PfbCst.NameStaticShadow)
				{
					continue;
				}

				if(child.name == "GenericModel")
				{
					child.SetActive(true);

					// アニメーションを終了時に固定する
					if(stayBroken)
					{
						child.GetComponent<Animator>().Play("Die", 0, 1);
					}
				}
				else
				{
					child.SetActive(false);
				}
			}
		}


		/// <summary>
		/// 死亡ログを取得する
		/// </summary>
		/// <returns></returns>
		private string GetDieLog(DmgBo dmgBo)
		{
			return SysTxtMng.ReplaceSubject(BattleMng.GetDeathTxt(dmgBo), this);
		}


		/// <summary>
		/// ユニットから死体（アイテム）になる
		/// </summary>
		private void BeCorpse()
		{
			// 影を死亡時の位置に動かす
			MoveShadow();

			// アイテムを生成する
			var corpse = ItemMng.GenerateItem(ItemCst.ItemIdCorpse);

			// 死体モジュールを初期化する
			corpse.GetModule<CorpseMdl>().Init(this);

			// プロパティを死ぬ前のユニットから継承する
			corpse.ChangeProp(PfbBean, ObjBean);

			// ObjectBeanでの死体になったときの処理
			corpse.ObjBean.ToCorpse(UnitBn.CorpseColliderType);

			// ユニットのオブジェクトをセットする
			uom.Init(uom.gameObject, corpse);

			// 位置をセットする
			corpse.ChangeMapPos(MapPos, true);

			// 死亡時のステート名を継承する
			corpse.dieStateName = dieStateName;

			// フィールドのアイテムリストに追加する
			FieldMng.FieldBo.AddItemWithoutSubscribe(corpse, false);

			// エフェクトを初期化する
			corpse.InitEffect();

			// アウトラインを徐々にアイテム用に変化させる
			ObjectMng.ToItemOutline(this);

			/// <summary>
			/// 影を死亡時の位置に動かす
			/// </summary>
			void MoveShadow()
			{
				var shadow = uom.transform.Find(PfbCst.NameStaticShadow);

				if(shadow != null)
				{
					shadow.transform.localPosition = PfbBean.DieShadowPosition;
				}
			}
		}


		/// <summary>
		/// 死亡アニメーションを再生する
		/// </summary>
		private void PlayDieAnimation(Subject<Unit> dieAnimationFinished)
		{
			if(ObjBean.IsFloating
				&& AnimationMng.HasAnimation(AnimatorCompo, AnimationCst.AniNameFloatingFall))
			{
				// 宙に浮かんでいるなら、落下する
				ActionFall(() => 
				{
					PlayAnimation(AnimationCst.StateFloatingDieAfterFalling, dieAnimationFinished);
				});
			}
			else
			{
				dieStateName = AnimationMng.RandomNormalStateName(AnimatorCompo, AnimState.Die);

				if(string.IsNullOrEmpty(dieStateName))
				{
					// 死亡アニメーションがないなら、Idleアニメーションを停止する
					ChangeAniSpd(0);

					dieAnimationFinished.OnNext(Unit.Default);
					dieAnimationFinished.OnCompleted();
				}
				else
				{
					animEvt.SubscribeOnFinish(dieAnimationFinished);
					PlayAnimation(dieStateName);
				}
			}
		}


		/// <summary>
		/// 落下する
		/// </summary>
		private void ActionFall(TweenCallback callback)
		{
			// 落下アニメーション開始
			AnimatorCompo.Play(AnimationCst.StateFloatingFall, 0);

			// 目的地
			var curPos = uom.UnityObject.transform.position;
			var destination = new Vector3(curPos.x, FieldMng.FieldBo.GetGridHeight(MapPos), curPos.z);

			// 落下時間
			float fallingDistance = Mathf.Abs(curPos.y - destination.y);
			float fallingDuration = fallingDistance / AnimationCst.FloatingFallingSpeed;

			// 地面まで時間をかけて落下する
			uom.transform.DOMove(destination, fallingDuration).OnComplete(callback);
		}


		/// <summary>
		/// 正面の座標を取得する
		/// </summary>
		public (int, int) GetFrontPos(int distance = 1)
		{
			(int, int) nextPos = MapPos;

			for(int i = 0; i < distance; i++)
			{
				nextPos = FieldMng.FieldBo.GetNextPos(nextPos, Dir);

				// フィールドの範囲外なら
				if(nextPos.Item1 == 0 || nextPos.Item2 == 0)
				{
					return default;
				}
			}

			return nextPos;
		}


		/// <summary>
		/// 装備可能な部位を取得する
		/// </summary>
		public List<EquipPartBo> GetEquipableParts()
		{
			var equipableParts = new List<EquipPartBo>(equipParts.Count);

			foreach(var part in equipParts)
			{
				// 装備できない部位なら
				if(!part.CheckEquipable())
				{
					continue;
				}

				equipableParts.Add(part);
			}

			return equipableParts;
		}


		/// <summary>
		/// 手の装備部位を取得する
		/// </summary>
		public List<EquipPartBo> GetEHands()
		{
			var equipableParts = new List<EquipPartBo>(2);

			foreach(var part in equipParts)
			{
				// 装備できない部位なら
				if(part.Part != EquipPart.Hand)
				{
					continue;
				}

				equipableParts.Add(part);
			}

			return equipableParts;
		}


		/// <summary>
		/// 両手持ちであるか
		/// </summary>
		/// <returns>true: 両手持ちである</returns>
		public bool Is2Handed(List<(EquipPartBo, ItemBo)> weapons = null)
		{
			if(weapons == null)
			{
				weapons = GetMeleeWeapons();
			}

			if(weapons.Count == 0)
			{
				return false;
			}

			// 手を１つでも負傷していたら両手持ちでない
			foreach(var hand in GetEHands())
			{
				var bpb = GetBodyPartBo(hand);

				if(bpb != null && bpb.GetInjuryDegree() == InjuryDegree.Severe)
				{
					return false;
				}
			}

			// 武器が１つだけ、その武器が両手持ちアニメーションを持つなら両手持ちになる
			return weapons.Count == 1 && weapons[0].Item2.WeaponBn.EquipAnim2 != EquipAnim.Default;
		}


		/// <summary>
		/// 装備している武器によってAnimator Layer Weightをセットする
		/// </summary>
		private void SetAnimatorLayerWeights()
		{
			Animator prevObjAnimator;

			Observable.TimerFrame(2).Subscribe(_ =>
			{
				if(AnimatorCompo == null)
				{
					return;
				}

				// プレビューオブジェクトのAnimator
				prevObjAnimator = CameraMng.SubCamPrevObj == null ?
					null : CameraMng.SubCamPrevObj.GetComponent<Animator>();

				// 全てのレイヤーをリセットする
				for(int i = 0; i < AnimatorCompo.layerCount; i++)
				{
					SetLayer(i, false);
				}

				// 装備している武器を取得する
				var weapons = GetMeleeWeapons();

				if(Is2Handed(weapons))
				{
					// 両手持ち
					switch(weapons[0].Item2.WeaponBn.EquipAnim2)
					{
						case EquipAnim.TwoHandedS:
							SetLayerFromName(AnimationCst.U1Layer2HandS, true);
							break;

						case EquipAnim.TwoHandedM:
							SetLayerFromName(AnimationCst.U1Layer2HandM, true);
							SetLayerFromName(AnimationCst.U1Layer2HandMMask, true);
							break;

						case EquipAnim.TwoHandedL:
							SetLayerFromName(AnimationCst.U1Layer2HandL, true);
							SetLayerFromName(AnimationCst.U1Layer2HandLMask, true);
							break;
					}
				}
				else
				{
					// 片手持ち
					foreach(var tuple in weapons)
					{
						// 右か左か
						bool isRight = tuple.Item1.partOrder == PartOrder.First;

						switch(tuple.Item2.WeaponBn.EquipAnim1)
						{
							case EquipAnim.OneHandedPole:
								SetLayerFromName(
									isRight ? AnimationCst.U1Layer1HandPoleRMask : AnimationCst.U1Layer1HandPoleLMask,
									true);
								break;
						}
					}
				}
			});

			/// <summary>
			/// レイヤーの状態を切り替える
			/// </summary>
			void SetLayer(int layerIdx, bool on)
			{
				float weight = on ? 1 : 0;
				AnimatorCompo.SetLayerWeight(layerIdx, weight);
				prevObjAnimator?.SetLayerWeight(layerIdx, weight);
			}

			/// <summary>
			/// レイヤーの状態を切り替える
			/// </summary>
			void SetLayerFromName(string layerName, bool on)
			{
				SetLayer(AnimatorCompo.GetLayerIndex(layerName), on);
			}
		}


		/// <summary>
		/// 指定アイテムを装備する
		/// </summary>
		public void Equip(EquipPartBo part, ItemBo equipment, bool refreshOutline)
		{
			// 指定部位に既に装備品があるなら、それを外す
			if(equipmentDict.ContainsKey(part))
			{
				Unequip(part);
			}

			// 装備品リストに加える
			try
			{
				equipmentDict.Add(part, equipment);
				equipmentList.Add(equipment);
			}
			catch(Exception) { }
			
			// インベントリから削除する
			RemoveItem(equipment, 1);

			// 持ち主は変わらないようにする
			equipment.owner = this;

			if(refreshOutline)
			{
				// アウトラインを更新する
				ObjectMng.RefreshOutline(this);
			}

			// 装備している武器によってAnimator Layer Weightをセットする
			SetAnimatorLayerWeights();
		}


		/// <summary>
		/// 指定アイテムを外す
		/// </summary>
		public void Unequip(EquipPartBo part, bool addToInventory = true)
		{
			// 装備品リストから削除し、インベントリに加える
			equipmentDict[part].OnUnequip();

			if(addToInventory)
			{
				AddItem(equipmentDict[part]);
			}

			equipmentList.Remove(equipmentDict[part]);
			equipmentDict.Remove(part);

			// 装備している武器によってAnimator Layer Weightをセットする
			SetAnimatorLayerWeights();
		}


		/// <summary>
		/// 指定の装備品を外す
		/// </summary>
		/// <returns>true: 外した</returns>
		public bool Unequip(ItemBo equipment)
		{
			bool removed = equipmentList.Remove(equipment);
			EquipPartBo part = null;

			if(removed)
			{
				foreach(var pair in equipmentDict)
				{
					if(pair.Value == equipment)
					{
						part = pair.Key;
						break;
					}
				}

				equipmentDict[part].OnUnequip();
				AddItem(equipment);
				equipmentDict.Remove(part);
			}

			// 装備している武器によってAnimator Layer Weightをセットする
			SetAnimatorLayerWeights();

			return removed;
		}


		/// <summary>
		/// 装備中の近接武器を取得する
		/// </summary>
		public List<(EquipPartBo, ItemBo)> GetMeleeWeapons()
		{
			var weapons = new List<(EquipPartBo, ItemBo)>();

			// 装備中のアイテムから近接武器であるものを抽出する
			foreach(var pair in equipmentDict)
			{
				if(pair.Value.IsMeleeWeapon())
				{
					weapons.Add((pair.Key, pair.Value));
				}
			}

			return weapons;
		}


		/// <summary>
		/// 装備中の遠隔武器を取得する
		/// </summary>
		public ItemBo GetRangedWeapon()
		{
			// 装備中のアイテムから遠隔武器であるものを抽出する
			foreach(var pair in equipmentDict)
			{
				if(pair.Value.IsRangedWeapon())
				{
					return pair.Value;
				}
			}

			return null;
		}


		/// <summary>
		/// 装備中の矢弾を取得する
		/// </summary>
		public ItemBo GetAmmo()
		{
			// 装備中のアイテムから武器フラグのあるものを抽出する
			foreach(var pair in equipmentDict)
			{
				if(pair.Value.IsAmmo())
				{
					return pair.Value;
				}
			}

			return null;
		}


		/// <summary>
		/// 装備品を取得する
		/// </summary>
		public List<ItemBo> GetEquipments()
		{
			var itemList = new List<ItemBo>();

			// 装備可能な部位を取得する
			var equipableParts = GetEquipableParts();

			foreach(var part in equipableParts)
			{
				ItemBo equipment = null;

				if(equipmentDict.TryGetValue(part, out equipment))
				{
					itemList.Add(equipment);
				}
			}

			return itemList;
		}


		/// <summary>
		/// 装備から装備部位を取得する
		/// </summary>
		public EquipPartBo GetPartFromEquipment(ItemBo eqp)
		{
			if(eqp == null)
			{
				return null;
			}

			foreach(var pair in equipmentDict)
			{
				if(pair.Value == eqp)
				{
					return pair.Key;
				}
			}

			return null;
		}


		/// <summary>
		/// 指定部位に装備しているアイテムを取得する
		/// </summary>
		public ItemBo GetEquipmentFromPart(EquipPartBo part)
		{
			if(part == null)
			{
				return null;
			}

			ItemBo equipment = null;
			equipmentDict.TryGetValue(part, out equipment);
			return equipment;
		}


		/// <summary>
		/// 指定部位に装備しているアイテムを取得する
		/// </summary>
		public ItemBo GetEquipmentFromPart(EquipPart part, PartOrder partOrder = PartOrder.First)
		{
			ItemBo equipment = null;

			foreach(var partBo in GetEquipableParts())
			{
				if(partBo.partOrder == partOrder && partBo.Part == part)
				{
					equipmentDict.TryGetValue(partBo, out equipment);
					break;
				}
			}

			return equipment;
		}


		/// <summary>
		/// 装備部位から身体部位を取得する
		/// </summary>
		public BodyPartBo GetBodyPartBo(EquipPartBo epb)
		{
			if(epb == null)
			{
				return null;
			}

			List<BodyPart> bodyPartCandidates = null;

			switch(epb.Part)
			{
				case EquipPart.Head:
					bodyPartCandidates = new List<BodyPart>() { BodyPart.Head };
					break;

				case EquipPart.Body:
					bodyPartCandidates = new List<BodyPart>() { BodyPart.Body };
					break;

				case EquipPart.Hand:
				case EquipPart.Arms:
					bodyPartCandidates = new List<BodyPart>() { BodyPart.ArmR, BodyPart.ArmL };
					break;

				case EquipPart.Legs:
					bodyPartCandidates = new List<BodyPart>() { BodyPart.LegR, BodyPart.LegL , 
						BodyPart.FrontLimbR, BodyPart.FrontLimbL, BodyPart.MidLimbR, 
						BodyPart.MidLimbL, BodyPart.HindLimbR, BodyPart.HindLimbL, 
						BodyPart.R1stLimb, BodyPart.L1stLimb, BodyPart.R2ndLimb, BodyPart.L2ndLimb, 
						BodyPart.R3rdLimb, BodyPart.L3rdLimb, BodyPart.R4thLimb, BodyPart.L4thLimb, };
					break;

				default:
					return null;
			}

			int order = 0;

			for(int i = 0; i < BodyParts.Count; i++)
			{
				if(bodyPartCandidates.Contains(BodyParts[i].Part))
				{
					order++;

					if(order == (int) epb.partOrder)
					{
						return BodyParts[i];
					}
				}
			}

			return null;
		}


		/// <summary>
		/// 身体部位に関連する武器の装備部位を取得する
		/// </summary>
		public EquipPartBo GetWeaponEquipPartBo(BodyPartBo bpb)
		{
			// 腕以外に武器は関連しない
			if(bpb.Part != BodyPart.ArmR && bpb.Part != BodyPart.ArmL)
			{
				return null;
			}

			// 指定部位が同種の身体部位の何番目にあたるか
			int order = (int) PartOrder.First;

			foreach(var val in BodyParts)
			{
				if(bpb == val)
				{
					break;
				}
				else if(bpb.LocalType == val.LocalType)
				{
					order++;
				}
			}
			
			// 装備部位から該当する順番のものを取り出す
			foreach(var val in equipParts)
			{
				if(val.Part != EquipPart.Hand)
				{
					continue;
				}
				else if((int) val.partOrder == order)
				{
					return val;
				}
			}

			return null;
		}


		/// <summary>
		/// 身体部位に関連する武器の装備部位を取得する
		/// </summary>
		public EquipPartBo GetArmorEquipPartBo(BodyPartBo bpb)
		{
			var equipPart = bpb.GetArmorPart();

			if(equipPart == EquipPart.None)
			{
				return null;
			}

			// 指定部位が同種の身体部位の何番目にあたるか
			int order = (int) PartOrder.First;

			foreach(var val in BodyParts)
			{
				if(bpb == val)
				{
					break;
				}
				else if(bpb.LocalType == val.LocalType)
				{
					order++;
				}
			}

			// 装備部位における順番
			int equipPartOrder = default;

			if(equipPart == EquipPart.Arms || equipPart == EquipPart.Legs)
			{
				equipPartOrder = order / 2;

				if(order % 2 == 1)
				{
					equipPartOrder++;
				}
			}

			// 該当部位を抽出する
			foreach(var val in equipParts)
			{
				if(val.Part != equipPart)
				{
					continue;
				}
				else if((int) val.partOrder == equipPartOrder)
				{
					return val;
				}
			}

			return null;
		}


		/// <summary>
		/// フィールド上からユニットを避難させる
		/// これを怠るとシーン遷移時のDontDestroyオブジェクトの座標が占有状態のままになる
		/// </summary>
		public void Evacuate()
		{
			MapPos = (FieldCst.MinMapPosX - 1, FieldCst.MinMapPosY - 1);
		}


		/// <summary>
		/// アニメーション速度を変える
		/// </summary>
		/// <param name="spd">速度倍率</param>
		public void ChangeAniSpd(float spd)
		{
			aniSpd = spd;
			AnimationMng.ChangeSpeed(AnimatorCompo, aniSpd);
		}


		/// <summary>
		/// アニメーション速度を元に戻す
		/// </summary>
		public void UndoAniSpd()
		{
			aniSpd = DefAniSpd;
			AnimationMng.ChangeSpeed(AnimatorCompo, aniSpd);
		}


		/// <summary>
		/// 指定した知能レベル以上かどうか
		/// </summary>
		public bool HasHigherInt(IntLevel intLevel)
		{
			return UnitBn.IntLvl >= intLevel;
		}


		/// <summary>
		/// 斬撃ブロック値、耐性を求める
		/// </summary>
		public (int, float) CalcSlashingDef(BodyPartBo bodyPartBo = null)
		{
			// 基礎防御
			float block = ArmorBn.BlockSlashing;
			float res = ArmorBn.ResSlashing;

			if(bodyPartBo != null)
			{
				// 該当部位の防具を取得する
				var armor = GetEquipmentFromPart(bodyPartBo.GetArmorPart());

				//防具の防御を足す
				if(armor != null)
				{
					block += armor.ArmorBn.BlockSlashing;
					res += armor.ArmorBn.ResSlashing;
				}
			}
			else
			{
				// 部位指定がない場合は全部位の平均を返す
				foreach(var bpb in BodyParts)
				{
					// 該当部位の防具を取得する
					var armor = GetEquipmentFromPart(bpb.GetArmorPart());

					if(armor != null)
					{
						//防具の防御を足す
						block += armor.ArmorBn.BlockSlashing;
						res += armor.ArmorBn.ResSlashing;
					}
					else
					{
						// 防具がない場合は基礎防御を足す
						block += ArmorBn.BlockSlashing;
						res += ArmorBn.ResSlashing;
					}
				}

				block /= BodyParts.Count;
				res /= BodyParts.Count;
			}

			return ((int) block, GeneralUtil.Round(res, 1));
		}


		/// <summary>
		/// 刺突ブロック値、耐性を求める
		/// </summary>
		public (int, float) CalcPiercingDef(BodyPartBo bodyPartBo = null)
		{
			// 基礎防御
			float block = ArmorBn.BlockPiercing;
			float res = ArmorBn.ResPiercing;

			if(bodyPartBo != null)
			{
				// 該当部位の防具を取得する
				var armor = GetEquipmentFromPart(bodyPartBo.GetArmorPart());

				//防具の防御を足す
				if(armor != null)
				{
					block += armor.ArmorBn.BlockPiercing;
					res += armor.ArmorBn.ResPiercing;
				}
			}
			else
			{
				// 部位指定がない場合は全部位の平均を返す
				foreach(var bpb in BodyParts)
				{
					// 該当部位の防具を取得する
					var armor = GetEquipmentFromPart(bpb.GetArmorPart());

					if(armor != null)
					{
						//防具の防御を足す
						block += armor.ArmorBn.BlockPiercing;
						res += armor.ArmorBn.ResPiercing;
					}
					else
					{
						// 防具がない場合は基礎防御を足す
						block += ArmorBn.BlockPiercing;
						res += ArmorBn.ResPiercing;
					}
				}

				block /= BodyParts.Count;
				res /= BodyParts.Count;
			}

			return ((int) block, GeneralUtil.Round(res, 1));
		}


		/// <summary>
		/// 衝撃ブロック値、耐性を求める
		/// </summary>
		public (int, float) CalcCrushingDef(BodyPartBo bodyPartBo = null)
		{
			// 基礎防御
			float block = ArmorBn.BlockCrushing;
			float res = ArmorBn.ResCrushing;

			if(bodyPartBo != null)
			{
				// 該当部位の防具を取得する
				var armor = GetEquipmentFromPart(bodyPartBo.GetArmorPart());

				//防具の防御を足す
				if(armor != null)
				{
					block += armor.ArmorBn.BlockCrushing;
					res += armor.ArmorBn.ResCrushing;
				}
			}
			else
			{
				// 部位指定がない場合は全部位の平均を返す
				foreach(var bpb in BodyParts)
				{
					// 該当部位の防具を取得する
					var armor = GetEquipmentFromPart(bpb.GetArmorPart());

					if(armor != null)
					{
						//防具の防御を足す
						block += armor.ArmorBn.BlockCrushing;
						res += armor.ArmorBn.ResCrushing;
					}
					else
					{
						// 防具がない場合は基礎防御を足す
						block += ArmorBn.BlockCrushing;
						res += ArmorBn.ResCrushing;
					}
				}

				block /= BodyParts.Count;
				res /= BodyParts.Count;
			}

			return ((int) block, GeneralUtil.Round(res, 1));
		}


		/// <summary>
		/// 炎ブロック値、耐性を求める
		/// </summary>
		public (int, float) CalcFireDef(BodyPartBo bodyPartBo = null)
		{
			// 基礎防御
			float block = ArmorBn.BlockFire;
			float res = ArmorBn.ResFire;

			if(bodyPartBo != null)
			{
				// 該当部位の防具を取得する
				var armor = GetEquipmentFromPart(bodyPartBo.GetArmorPart());

				//防具の防御を足す
				if(armor != null)
				{
					block += armor.ArmorBn.BlockFire;
					res += armor.ArmorBn.ResFire;
				}
			}
			else
			{
				// 部位指定がない場合は全部位の平均を返す
				foreach(var bpb in BodyParts)
				{
					// 該当部位の防具を取得する
					var armor = GetEquipmentFromPart(bpb.GetArmorPart());

					if(armor != null)
					{
						//防具の防御を足す
						block += armor.ArmorBn.BlockFire;
						res += armor.ArmorBn.ResFire;
					}
					else
					{
						// 防具がない場合は基礎防御を足す
						block += ArmorBn.BlockFire;
						res += ArmorBn.ResFire;
					}
				}

				block /= BodyParts.Count;
				res /= BodyParts.Count;
			}

			return ((int) block, GeneralUtil.Round(res, 1));
		}


		/// <summary>
		/// 雷ブロック値、耐性を求める
		/// </summary>
		public (int, float) CalcLightningDef(BodyPartBo bodyPartBo = null)
		{
			// 基礎防御
			float block = ArmorBn.BlockLightning;
			float res = ArmorBn.ResLightning;

			if(bodyPartBo != null)
			{
				// 該当部位の防具を取得する
				var armor = GetEquipmentFromPart(bodyPartBo.GetArmorPart());

				//防具の防御を足す
				if(armor != null)
				{
					block += armor.ArmorBn.BlockLightning;
					res += armor.ArmorBn.ResLightning;
				}
			}
			else
			{
				// 部位指定がない場合は全部位の平均を返す
				foreach(var bpb in BodyParts)
				{
					// 該当部位の防具を取得する
					var armor = GetEquipmentFromPart(bpb.GetArmorPart());

					if(armor != null)
					{
						//防具の防御を足す
						block += armor.ArmorBn.BlockLightning;
						res += armor.ArmorBn.ResLightning;
					}
					else
					{
						// 防具がない場合は基礎防御を足す
						block += ArmorBn.BlockLightning;
						res += ArmorBn.ResLightning;
					}
				}

				block /= BodyParts.Count;
				res /= BodyParts.Count;
			}

			return ((int) block, GeneralUtil.Round(res, 1));
		}


		/// <summary>
		/// 腐食ブロック値、耐性を求める
		/// </summary>
		public (int, float) CalcCorrosiveDef(BodyPartBo bodyPartBo = null)
		{
			// 基礎防御
			float block = ArmorBn.BlockCorrosive;
			float res = ArmorBn.ResCorrosive;

			if(bodyPartBo != null)
			{
				// 該当部位の防具を取得する
				var armor = GetEquipmentFromPart(bodyPartBo.GetArmorPart());

				//防具の防御を足す
				if(armor != null)
				{
					block += armor.ArmorBn.BlockCorrosive;
					res += armor.ArmorBn.ResCorrosive;
				}
			}
			else
			{
				// 部位指定がない場合は全部位の平均を返す
				foreach(var bpb in BodyParts)
				{
					// 該当部位の防具を取得する
					var armor = GetEquipmentFromPart(bpb.GetArmorPart());

					if(armor != null)
					{
						//防具の防御を足す
						block += armor.ArmorBn.BlockCorrosive;
						res += armor.ArmorBn.ResCorrosive;
					}
					else
					{
						// 防具がない場合は基礎防御を足す
						block += ArmorBn.BlockCorrosive;
						res += ArmorBn.ResCorrosive;
					}
				}

				block /= BodyParts.Count;
				res /= BodyParts.Count;
			}

			return ((int) block, GeneralUtil.Round(res, 1));
		}


		/// <summary>
		/// 魔法ブロック値、耐性を求める
		/// </summary>
		public (int, float) CalcMagicDef(BodyPartBo bodyPartBo = null)
		{
			// 基礎防御
			float block = ArmorBn.BlockMagic;
			float res = ArmorBn.ResMagic;

			if(bodyPartBo != null)
			{
				// 該当部位の防具を取得する
				var armor = GetEquipmentFromPart(bodyPartBo.GetArmorPart());

				//防具の防御を足す
				if(armor != null)
				{
					block += armor.ArmorBn.BlockMagic;
					res += armor.ArmorBn.ResMagic;
				}
			}
			else
			{
				// 部位指定がない場合は全部位の平均を返す
				foreach(var bpb in BodyParts)
				{
					// 該当部位の防具を取得する
					var armor = GetEquipmentFromPart(bpb.GetArmorPart());

					if(armor != null)
					{
						//防具の防御を足す
						block += armor.ArmorBn.BlockMagic;
						res += armor.ArmorBn.ResMagic;
					}
					else
					{
						// 防具がない場合は基礎防御を足す
						block += ArmorBn.BlockMagic;
						res += ArmorBn.ResMagic;
					}
				}

				block /= BodyParts.Count;
				res /= BodyParts.Count;
			}

			return ((int) block, GeneralUtil.Round(res, 1));
		}


		/// <summary>
		/// 精神ブロック値、耐性を求める
		/// </summary>
		public (int, float) CalcMentalDef(BodyPartBo bodyPartBo = null)
		{
			// 基礎防御
			float block = ArmorBn.BlockMental;
			float res = ArmorBn.ResMental;

			if(bodyPartBo != null)
			{
				// 該当部位の防具を取得する
				var armor = GetEquipmentFromPart(bodyPartBo.GetArmorPart());

				//防具の防御を足す
				if(armor != null)
				{
					block += armor.ArmorBn.BlockMental;
					res += armor.ArmorBn.ResMental;
				}
			}
			else
			{
				// 部位指定がない場合は全部位の平均を返す
				foreach(var bpb in BodyParts)
				{
					// 該当部位の防具を取得する
					var armor = GetEquipmentFromPart(bpb.GetArmorPart());

					if(armor != null)
					{
						//防具の防御を足す
						block += armor.ArmorBn.BlockMental;
						res += armor.ArmorBn.ResMental;
					}
					else
					{
						// 防具がない場合は基礎防御を足す
						block += ArmorBn.BlockMental;
						res += ArmorBn.ResMental;
					}
				}

				block /= BodyParts.Count;
				res /= BodyParts.Count;
			}

			return ((int) block, GeneralUtil.Round(res, 1));
		}


		/// <summary>
		/// 受け流し値を求める
		/// </summary>
		public int CalcFendOffVal()
		{
			int val = ArmorBn.FendOffVal;

			// 装備品から防具を取り出し処理する
			foreach(var pair in equipmentDict)
			{
				if(!pair.Value.ItemBn.ArmorFlg)
				{
					continue;
				}

				val += pair.Value.ArmorBn.FendOffVal;
			}

			return val;
		}


		/// <summary>
		/// 記憶子を放出する
		/// </summary>
		protected void ReleaseMemorino()
		{
			if(Visible)
			{
				// 放出エフェクト
				EffectMng.SpawnMemorinoUp(this);
			}

			// 放出される記憶子を求める
			var memorino = CalcDeathMemorino();

			// プレイヤーが記憶子を吸収する
			PlayerMng.Player.AbsorbMemorino(memorino);
		}


		/// <summary>
		/// 記憶子を吸収する
		/// </summary>
		protected void AbsorbMemorino(int memorino)
		{
			// 吸収エフェクト
			EffectMng.SpawnVfx(VfxId.MemorinoDown, this);

			// 所持記憶子を増やす
			ChangeMemorino(memorino);
		}


		/// <summary>
		/// 所持記憶子を増減する
		/// </summary>
		/// <param name="changeVal">変化量</param>
		public void ChangeMemorino(int changeVal)
		{
			var tmpVal = Memorino + changeVal;

			// 境界値を超えないようにする。オーバーフローは考慮していない
			if(tmpVal < UnitCst.MinMemorino)
			{
				tmpVal = UnitCst.MinMemorino;
			}
			else if(tmpVal > UnitCst.MaxMemorino)
			{
				tmpVal = UnitCst.MaxMemorino;
			}

			Memorino = tmpVal;

			// プレイヤーならHUDに表示する
			if(IsPlayer())
			{
				MemorinoMng.ChangeMemorino(changeVal);
			}
		}


		/// <summary>
		/// このユニットが死んだ時に放出する記憶子の量を求める
		/// </summary>
		public int CalcDeathMemorino()
		{
			// 倍率
			float magnification = 0.8f;

			int multiple10 = (Lvl - 1) / 10 + 1;
			int excess = Lvl % 10;

			for(int i = 1; i <= multiple10; i++)
			{
				float increment = 0.2f * i;
				magnification += increment * 10;

				if(i == multiple10 && excess > 0)
				{
					// 超過分を引く
					magnification -= increment * (10 - excess);
				}
			}

			int memorino = Mathf.RoundToInt(UnitBn.DefMemorino * GeneralUtil.Round(magnification));
			memorino = memorino < 1 ? 1 : memorino;

			return memorino;
		}


		/// <summary>
		/// 射撃を試みる
		/// </summary>
		/// <returns>true: 射撃した</returns>
		public void TryShoot()
		{
			// 装備中の矢弾、遠隔武器を取得する
			var weapon = GetRangedWeapon();
			var ammo = GetAmmo();

			if(weapon == null)
			{
				// 遠隔武器を装備していないなら射撃しない
				// プレイヤーならログを残す
				if(IsPlayer())
				{
					MsgBoardMng.Log(BattleMng.GetMiscTxt(BattleMiscTxt.NoRangedWeapon));
				}

				return;
			}
			else if(ammo == null)
			{
				// 矢弾を装備していないなら射撃しない
				// プレイヤーならログを残す
				if(IsPlayer())
				{
					MsgBoardMng.Log(BattleMng.GetMiscTxt(BattleMiscTxt.NoAmmo));
				}

				return;
			}

			if(Ai.TargetDat.Target == null || !(Ai.TargetDat.Target is UnitBo) 
				|| ((UnitBo) Ai.TargetDat.Target).isDead)
			{
				// 見えている中で最も近い敵をランダムに取得する
				var nearestEnemy = Ai.RandomNearestUnit(FavorType.Hostile, true);

				if(nearestEnemy == null)
				{
					// 敵がいないなら、射撃しない
					// プレイヤーならログを残す
					if(IsPlayer())
					{
						MsgBoardMng.Log(BattleMng.GetMiscTxt(BattleMiscTxt.FireNoTarget));
					}

					return;
				}
				else
				{
					Ai.TargetDat.Target = nearestEnemy;
				}
			}

			// 通常攻撃したログを残す
			var txt = BattleMng.GetNormalAttackTxt(weapon.WeaponBn.NormalAttackTxtId);
			var parameters = new List<object>() { ((UnitBo) Ai.TargetDat.Target).GetName() };
			MsgBoardMng.Log(SysTxtMng.Replace(txt, this, parameters), true);

			var targetPos = Ai.TargetDat.Target.MapPos;

			// 遠隔武器の命中値を求める
			int accuracyVal = CalcRangedAtkAccuracy(weapon);
			
			// 命中値が0以下なら標的の周囲1マス以内のマスに標的を変える
			if(accuracyVal <= 0)
			{
				// 標的の周囲1マス以内のマス
				var mapPosList = FieldMng.GetMapPosFrom(Ai.TargetDat.Target.MapPos, 1);

				// 射撃者の座標は対象外とする
				mapPosList.Remove(MapPos);

				// 新たな座標を抽選する
				targetPos = GeneralUtil.Random(mapPosList);
			}
			
			// 射撃する
			Shoot(ammo, targetPos, accuracyVal);
		}


		/// <summary>
		/// 遠隔武器の命中値を求める
		/// </summary>
		private int CalcRangedAtkAccuracy(ItemBo weapon)
		{
			// 射撃の命中値を求める
			int accuracyVal = CalcAccuracyValue(weapon.WeaponBn);

			// 標的までの距離
			var distance = FieldMng.GetDistance(MapPos, Ai.TargetDat.Target.MapPos);

			if(distance <= 1)
			{
				// 隣接するマスなら命中値を下げる
				distance -= UnitCst.AccuDecMelee;
			}
			else
			{
				// 閾値射程と標的の距離
				var thrRangeToTarget = distance - weapon.WeaponBn.ThresholdRange;

				// 標的が閾値射程外にあるなら、その距離だけ命中値を下げる
				if(thrRangeToTarget > 0)
				{
					// 射程と閾値射程の差
					var rangeDiff = weapon.WeaponBn.Range - weapon.WeaponBn.ThresholdRange;

					if(rangeDiff > 0)
					{
						// 1マス毎の命中減衰値。射程外で0になるように
						var decVal = 100f / (rangeDiff + 1);

						accuracyVal -= (int) (thrRangeToTarget * decVal);
					}
				}
			}

			return accuracyVal;
		}


		/// <summary>
		/// 射撃する
		/// </summary>
		/// <param name="projectile">投射物</param>
		/// <param name="targetPos">標的となる座標</param>
		/// <param name="accuracyVal">命中値</param>
		public void Shoot(ItemBo projectile, (int, int) targetPos, int accuracyVal)
		{
			// ユニットの座標と対象座標を結ぶ線上の座標のリストを求める
			var posList = LineMng.GetPosOnLine(MapPos, targetPos, false);

			// 一番最初に射線を遮る物体を取得する
			var obstacle = FieldMng.FieldBo.GetObstacle(posList);
			
			if(obstacle == null)
			{
				// 物体が存在しないなら
				// 地面に射撃する
				ShootToGround(projectile, targetPos);
			}
			else if(obstacle is UnitBo)
			{
				// 物体がユニットなら
				ShootToUnit(projectile, (UnitBo) obstacle, accuracyVal);
			}
			else if(obstacle is ItemBo)
			{
				// 物体がアイテムなら
				ShootToItem(projectile, (ItemBo) obstacle);
			}
			else if(obstacle is WallBo)
			{
				// 物体が壁なら
				ShootToWall(projectile, (WallBo) obstacle);
			}
		}


		/// <summary>
		/// 地面に射撃する
		/// </summary>
		private void ShootToGround(ItemBo projectile, (int, int) targetPos, bool logMissAtk = false)
		{
			IsActing = true;

			// 衝突用のダミーオブジェクトを生成する
			var dummy = new GameObject();
			dummy.transform.position = FieldMng.ToWorldCoordCenter(targetPos);

			// コライダーをつける
			var collider = dummy.AddComponent<SphereCollider>();
			collider.radius = UnitCst.GroundDummyRadius;

			var onTargetHit = new Subject<Unit>();

			// 投射物を標的に投射する
			var projectileObj = ProjectileArrow(projectile, dummy, onTargetHit, false);

			onTargetHit.Subscribe(_ =>
			{
				if(logMissAtk)
				{
					// 攻撃が外れたときの処理
					//LogMissAtk();
				}

				// ダミーを消す
				GameObject.Destroy(dummy);

				// 矢が地面に刺さったログを残す
				MsgBoardMng.Log(BattleMng.GetMiscTxt(BattleMiscTxt.ArrowGround), false);

				// 矢のオブジェクトを消す
				GameObject.Destroy(projectileObj);

				// 放った矢弾はフィールドに落ちる
				var ammo = ItemMng.Clone(projectile);
				ammo.stack = 1;
				FieldMng.FieldBo.AddItem(ammo, targetPos);

				IsActing = false;

				// ターン終了
				StartEndingTurn();
			});
		}


		/// <summary>
		/// ユニットに射撃する
		/// </summary>
		/// <param name="projectile">投射物</param>
		/// <param name="target">標的</param>
		/// <param name="accuracyVal">命中値</param>
		public void ShootToUnit(ItemBo projectile, UnitBo target, int accuracyVal)
		{
			bool isFendOff = false;

			if(IsHit(accuracyVal, target, out isFendOff))
			{
				// 命中したら
				IsActing = true;

				// パリィされたか
				var isParried = BattleMng.IsParried(target);
				
				// 当たった後の標的側の処理
				var onTargetHit = new Subject<Unit>();

				// 射撃する
				var arrowObj = 
					ProjectileArrow(projectile, target.uom.UnityObject, onTargetHit, !isParried);

				onTargetHit.Subscribe(_ =>
				{
					if(isParried)
					{
						// パリィする
						//target.LogParry(this);

						// 矢のオブジェクトを消す
						GameObject.Destroy(arrowObj);

						IsActing = false;

						// ターン終了
						StartEndingTurn();
					}
					else
					{
						// 矢に当たったSEを再生する
						SoundMng.PlaySe(target, projectile.WeaponBn.RandomHitSe());

						// 攻撃情報を求める
						var weapon = GetRangedWeapon();
						var atkBo = BattleMng.CalcAtkBo(this, weapon, projectile.WeaponBn);

						// ダメージ情報を求める
						var dmgBo = BattleMng.CalcDmgBo(atkBo, target, isFendOff, DmgType.Ranged);

						// 攻撃の方向
						dmgBo.hitDir = target.GetHitDir(FieldMng.GetDirection(MapPos, target.MapPos));

						// ログは処理開始時に出す
						var whenStart = new Subject<Unit>();
						whenStart.Subscribe(_2 =>
						{
							// かすり傷のログ
							if(isFendOff)
							{
								MsgBoardMng.Log(BattleMng.GetMiscTxt(BattleMiscTxt.Graze), false);
							}
						});

						// ヒットアニメーション終了後の処理
						var hitFinished = new Subject<Unit>();
						hitFinished.Subscribe(_3 =>
						{
							IsActing = false;

							// ターン終了
							StartEndingTurn();

						});

						// 戦闘処理を登録
					}
				});
			}
			else
			{
				// 攻撃が外れたら
				// 地面に射撃する
				ShootToGround(projectile, target.MapPos, true);
			}
		}


		/// <summary>
		/// 障害となるアイテムに射撃する
		/// </summary>
		private void ShootToItem(ItemBo projectile, ItemBo target)
		{
			IsActing = true;

			var onTargetHit = new Subject<Unit>();

			onTargetHit.Subscribe(_ =>
			{
				IsActing = false;

				// ターン終了
				StartEndingTurn();
			});

			ProjectileArrow(projectile, target.uom.UnityObject, onTargetHit, true);
		}


		/// <summary>
		/// 壁に射撃する
		/// </summary>
		private void ShootToWall(ItemBo projectile, WallBo target)
		{
			IsActing = true;

			var onTargetHit = new Subject<Unit>();

			onTargetHit.Subscribe(_ =>
			{
				IsActing = false;

				// ターン終了
				StartEndingTurn();
			});

			ProjectileArrow(projectile, target.gameObject, onTargetHit, true);
		}


		/// <summary>
		/// 矢を標的に投射する
		/// </summary>
		/// <param name="projectile">投射物のゲームオブジェクト</param>
		/// <param name="target">標的のゲームオブジェクト</param>
		/// <param name="onTargetHit">標的に当たった後の処理</param>
		/// <param name="isChild">投射物を衝突対象の子にするか</param>
		/// <returns>投射物のゲームオブジェクト</returns>
		private GameObject ProjectileArrow(ItemBo projectile, GameObject target,
			Subject<Unit> onTargetHit, bool isChild)
		{
			// 矢のオブジェクトを生成する
			var worldPos = projectile.ToWorldPos(MapPos) + GameCst.ProjectilePosCalib;
			var projectileObj = ObjectMng.Embody(projectile, worldPos);

			// 矢の投射パラメータを設定する
			var cb1 = new ColliderBean(projectileObj, UnitCst.ArrowSpd, isChild);
			cb1.changeDir = true;

			// 被投射側の投射パラメータを設定する
			var cb2 = new ColliderBean(target, onTargetHit);

			var uom = target.GetComponent<UnityObjectMediator>();

			if(uom != null)
			{
				cb2.centerCalibY = uom.Script.PfbBean.CenterCalibY;
			}

			// 矢を標的に投射する
			GeneralUtil.Projectile(cb1, cb2);

			return projectileObj;
		}


		/// <summary>
		/// ログなどで使う名前を取得する
		/// </summary>
		/// <param name="isSecondPerson">true: 二人称を取得する</param>
		public override string GetName(bool isSecondPerson = true)
		{
			if(isSecondPerson && IsPlayer())
			{
				return  SysTxtMng.GetTxt(SysTxtId.PlayerSecondPerson);
			}
			else if(!UnitBn.Unique)
			{
				// ユニークでないなら名前をそのまま
				return UnitBn.Name;
			}

			return NicknameMng.Compose(UnitBn.Name, new AliasBo(UnitBn.Alias, UnitBn.IsAdjective));
		}


		/// <summary>
		/// 周囲を照らす
		/// </summary>
		public override void EnlightSurroundings()
		{
			base.EnlightSurroundings();

			// 装備品の効果を適用する
			foreach(var pair in equipmentDict)
			{
				pair.Value.EnlightSurroundings();
			}
		}


		/// <summary>
		/// 料理を開始する
		/// </summary>
		/// <param name="recipe">レシピ情報</param>
		/// <param name="igrdtInfo">使う材料</param>
		/// <param name="set">作る料理のセット数</param>
		public void Cook(RecipeBean recipe, Dictionary<ItemBo, int> igrdtInfo, int set)
		{
			// 料理を生成する
			var dish = recipe.GenerateDish(set);

			// 料理開始ログ
			var prms = new List<object>() { dish.GetName() };
			var log = SysTxtMng.ReplaceParams(SysTxtMng.GetTxt(SysTxtId.StartCook), prms);
			MsgBoardMng.Log(log);

			// 料理音
			var queueNum = SoundMng.PlaySe(this, recipe.CookSe, true);

			// 料理アニメーション

			// ターンラプス後の処理を登録する
			var turnlapseCompleted = new Subject<Unit>();

			turnlapseCompleted.Subscribe(_ =>
			{
				// 料理完成ログ
				var prms2 = new List<object>() { dish.GetName(), dish.stack };
				var log2 = SysTxtMng.ReplaceParams(SysTxtMng.GetTxt(SysTxtId.CookSuccessed), prms2);
				MsgBoardMng.Log(log2);
				
				// 料理音を消す
				SoundMng.StopSe(queueNum);

				// 料理成功SE
				SoundMng.PlaySe(this, SeId.CookSuccess);

				// 材料を持ち物から削除する
				foreach(var pair in igrdtInfo)
				{
					if(pair.Key.IsDrinkable())
					{
						// 飲み物なら中身を減らす
						RemoveLiquid(pair.Key, pair.Value);
					}
					else
					{
						RemoveItem(pair.Key, pair.Value);
					}
				}

				// 使用した瓶
				ItemBo bottle = null;

				// 瓶に入りきらなかった液量
				int remainingQuant = 0;

				if(dish.ItemBn.DrinkFlg)
				{
					// 飲み物を持ち物に加える
					remainingQuant = AddLiquid(dish, dish.stack, ref bottle);
				}
				else
				{
					// 料理を持ち物に追加する
					AddItem(dish);
				}

				var menuClosed = new Subject<Unit>();
				menuClosed.Subscribe(_2 =>
				{
					// 瓶に入りきらない液体があったら
					if(remainingQuant > 0)
					{
						// 残り物を飲むイベント
						var evt = new EvtDrinkExtra();
						evt.Arise(new EvtArgDrinkExtra(dish, remainingQuant));
					}
				});

				// アイテムプレビューメニューを生成する
				var menu = MenuMng.InstantiateMenu(MenuCst.NameMenuItemPrev);

				// メニューを開く
				var menuScript = menu.AddComponent<MenuItemPrev>();
				menuScript.Open(bottle != null ? bottle : dish, menuClosed);
			});

			// ターンラプス中断時の処理を登録する
			var turnlapseCanceled = new Subject<Unit>();

			turnlapseCanceled.Subscribe(_ =>
			{
				// 料理音を消す
				SoundMng.StopSe(queueNum);

				// 動作モーションを終了する
			});

			// 料理時間
			var cookDuration = (int) (recipe.Time * (1 + FoodCst.CookDurationIncrRate * set));
			
			// ターンラプス開始
			TurnMng.StartTurnlapse(cookDuration, turnlapseCompleted, turnlapseCanceled);
		}


		/// <summary>
		/// インベントリの瓶の液体を減らす
		/// </summary>
		/// <param name="bottles">瓶</param>
		/// <param name="num">減らす数</param>
		private void RemoveLiquid(ItemBo bottles, int num)
		{
			// 1つの瓶に入っている液体の量
			var bottleMdl = bottles.GetModule<BottleMdl>();
			int quantPerBttl = bottleMdl.Quantity;

			// スタックしている全ての瓶に入っている液体の総量
			int quantSum = quantPerBttl * bottles.stack;

			// 瓶から出す総量
			int removeSum = num > quantSum ? quantSum : num;

			// 使い切る瓶の量
			int useUpBttlNum = removeSum / quantPerBttl;
			
			if(useUpBttlNum != 0)
			{
				// 使い切った瓶があれば分離する
				var usedBottles = ItemMng.Clone(bottles);
				usedBottles.stack = useUpBttlNum;
				RemoveItem(bottles, useUpBttlNum);

				// 分離した瓶の中身を空にする
				var usedBottleMdl = usedBottles.GetModule<BottleMdl>();
				usedBottleMdl.Remove(quantSum);

				// 分離した瓶を手持ちに加える
				AddItem(usedBottles);
			}

			// 全ての瓶を使い終わったなら処理終了
			if(bottles.stack < 1)
			{
				return;
			}

			// 最後の1つの瓶の減らされる量
			int lastDecNum = removeSum % quantPerBttl;

			if(lastDecNum == 0)
			{
				return;
			}

			// 最後の瓶を分離する
			var lastBottle = ItemMng.Clone(bottles);
			lastBottle.stack = 1;
			RemoveItem(bottles, 1);

			// 最後の瓶の液量をセットする
			var lastBottleMdl = lastBottle.GetModule<BottleMdl>();
			lastBottleMdl.Remove(lastDecNum);

			// 最後の瓶を手持ちに加える
			AddItem(lastBottle);
		}


		/// <summary>
		/// 液体をインベントリに加える
		/// </summary>
		/// <returns>入れ損ねた液体の量</returns>
		private int AddLiquid(ItemBo liquid, int quantity, ref ItemBo usedBottle)
		{
			int remainingQaunt = quantity;

			foreach(var item in inventory)
			{
				var bottleMdl = item.GetModule<BottleMdl>();

				// 瓶でないなら処理しない
				if(bottleMdl == null)
				{
					continue;
				}

				// 指定の液体を入れられないなら処理しない
				if(!bottleMdl.IsAddable(liquid))
				{
					continue;
				}

				usedBottle = item;

				// 液体を瓶に入れる
				var addedQuant = bottleMdl.Add(liquid, remainingQaunt);

				// 入れた量を引く
				remainingQaunt -= addedQuant;

				// 液体を全て入れ切ったら処理を終える
				if(remainingQaunt < 1)
				{
					break;
				}
			}

			return remainingQaunt;
		}


		/// <summary>
		/// 持ち物にアイテムを追加する
		/// </summary>
		public void AddItem(ItemBo item)
		{
			if(item == null || item.stack < 1)
			{
				return;
			}

			if(!ItemMng.Stack(inventory, item))
			{
				// 挿入するインデックス
				int idx = inventory.Count;

				string comparingId1 = item.ItemBn.ObjId;
				var bottleMdl = item.GetModule<BottleMdl>();

				// 瓶なら中身の液体のIDを使う
				if(bottleMdl != null && bottleMdl.HasLiquid())
				{
					comparingId1 = bottleMdl.Liquid.ItemBn.ObjId;
				}

				for(int i = inventory.Count - 1; i >= 0; i-- )
				{
					string comparingId2 = inventory[i].ItemBn.ObjId;
					var bottleMdl2 = inventory[i].GetModule<BottleMdl>();

					// 瓶なら中身の液体のIDを使う
					if(bottleMdl2 != null && bottleMdl2.HasLiquid())
					{
						comparingId2 = bottleMdl2.Liquid.ItemBn.ObjId;
					}

					// IDの数字の大きさを比べる
					var comparison = GeneralUtil.Compare(comparingId2, comparingId1);

					if(comparison == NumComparison.Lesser)
					{
						// 追加するアイテムのIDが参照中のアイテムのIDより小さいなら次のアイテムを見る
						idx--;
						continue;
					}
					else
					{
						break;
					}
				}
				
				inventory.Insert(idx, item);
			}

			// 持ち主をセットする
			item.owner = this;
		}


		/// <summary>
		/// 持ち物からアイテムを削除する
		/// </summary>
		/// <param name="item">アイテム</param>
		/// <param name="decNum">削除する数</param>
		public void RemoveItem(ItemBo item, int decNum = 0)
		{
			// 手持ちのアイテムでないなら処理しない
			for(int i = 0; i < inventory.Count; i++)
			{
				if(inventory[i] == item)
				{
					break;
				}

				if(i == inventory.Count - 1)
				{
					return;
				}
			}

			if(decNum < 1)
			{
				decNum = item.stack;
			}

			if(item.stack > decNum)
			{
				// スタック数を減らす
				item.stack -= decNum;
			}
			else
			{
				//スタック数がなくなるなら持ち物から削除する
				inventory.Remove(item);

				// 持ち主をクリアする
				item.owner = null;
			}
		}


		/// <summary>
		/// Animatorに釣りフラグをセットする
		/// </summary>
		public void SetFishingFlg(bool val)
		{
			AnimatorCompo.SetBool(AnimationCst.PrmtIsFishing, val);
		}


		/// <summary>
		/// うずくまるモーションを切り替える
		/// </summary>
		public void SwitchCrouching(bool val)
		{
			AnimatorCompo.SetBool(AnimationCst.PrmtIsCrouching, val);
		}


		/// <summary>
		/// うずくまり中か
		/// </summary>
		public bool IsCrouching()
		{
			return AnimatorCompo.GetBool(AnimationCst.PrmtIsCrouching);
		}


		/// <summary>
		/// スケルトンのパーツオブジェクトを取得する
		/// </summary>
		public Transform GetSklObj(SkeletonPart skeletonPart)
		{
			if(uom == null || uom.UnityObject == null)
			{
				return null;
			}

			string sklName = default;

			switch(skeletonPart)
			{
				case SkeletonPart.HandL:
					sklName = UnitCst.SklNameHandL;
					break;

				case SkeletonPart.HandR:
					sklName = UnitCst.SklNameHandR;
					break;
			}

			return uom.UnityObject.transform.Find(sklName);
		}


		/// <summary>
		/// 指定したパーツにある武器のオブジェクトを取得する
		/// </summary>
		/// <returns>なければnull</returns>
		public GameObject GetWeaponObj(SkeletonPart skeletonPart)
		{
			var sklHand = GetSklObj(skeletonPart);

			foreach(Transform tf in sklHand)
			{
				return tf.gameObject;
			}

			return null;
		}


		/// <summary>
		/// 指定座標の方に向く
		/// </summary>
		public void TurnTo((int, int) target)
		{
			if(MapPos == default)
			{
				return;
			}

			// 指定座標とユニットの座標の差
			var diffX = target.Item1 - MapPos.Item1;
			var diffY = target.Item2 - MapPos.Item2;

			// 各座標値の差の差
			var diffDiff = Mathf.Abs(diffX) - Mathf.Abs(diffY);
			var parameter = diffDiff > 0 ? diffX : diffY;
			var ddRate = diffDiff == 0 ? 0 : Mathf.Abs((float) diffDiff / parameter);

			Direction dir = default;
			
			if(diffX > 0)
			{
				if(diffY > 0)
				{
					if(ddRate < 0.5f)
					{
						dir = Direction.UpRight;
					}
					else
					{
						if(diffDiff > 0)
						{
							dir = Direction.Right;
						}
						else
						{
							dir = Direction.Up;
						}
					}
				}
				else
				{
					if(ddRate < 0.5f)
					{
						dir = Direction.DownRight;
					}
					else
					{
						if(diffDiff > 0)
						{
							dir = Direction.Right;
						}
						else
						{
							dir = Direction.Down;
						}
					}
				}
			}
			else
			{
				if(diffY > 0)
				{
					if(ddRate < 0.5f)
					{
						dir = Direction.UpLeft;
					}
					else
					{
						if(diffDiff > 0)
						{
							dir = Direction.Left;
						}
						else
						{
							dir = Direction.Up;
						}
					}
				}
				else
				{
					if(ddRate < 0.5f)
					{
						dir = Direction.DownLeft;
					}
					else
					{
						if(diffDiff > 0)
						{
							dir = Direction.Left;
						}
						else
						{
							dir = Direction.Down;
						}
					}
				}
			}

			// ユニットの向きを変える
			Rotate(dir, false);
		}


		/// <summary>
		/// 人ユニットであるか
		/// </summary>
		/// <returns>true: 人ユニットである</returns>
		public bool IsHuman()
		{
			return GetModule<HumanMdl>() != null;
		}


		/// <summary>
		/// 骸骨ユニットであるか
		/// </summary>
		/// <returns>true: はい</returns>
		public bool IsSkeleton()
		{
			return GetModule<SkeletonMdl>() != null;
		}


		/// <summary>
		/// 常駐スキルを活性状態を切り替える
		/// </summary>
		/// <returns>削除したスキル</returns>
		public ASustainSkillBo SwitchActivationSustainSkill(ASustainSkillBo skill)
		{
			// 非活性化
			foreach(var curSkill in sustainSkills)
			{
				if(curSkill.SkillBn.SkillId == skill.SkillBn.SkillId)
				{
					sustainSkills.Remove(curSkill);

					return curSkill;
				}
			}

			// 活性化
			sustainSkills.Add(skill);

			return null;
		}


		/// <summary>
		/// 指定の常駐スキルを使っているかを調べる
		/// </summary>
		/// <returns>true: 使っている</returns>
		public bool UsingSustainSkill(SkillId skillId)
		{
			foreach(var curSkill in sustainSkills)
			{
				if(curSkill.SkillBn.SkillId == skillId)
				{
					return true;
				}
			}

			return false;
		}


		/// <summary>
		/// 常駐スキルのセーブデータを作成する
		/// </summary>
		public XElement MakeSaveDataSustainSkill()
		{
			var saveData = new XElement(SaveCst.SdeSustainSkills);

			foreach(var skill in sustainSkills)
			{
				saveData.Add(new XElement(SaveCst.SdeSkillId, (int) skill.SkillBn.SkillId));
			}

			return saveData;
		}


		/// <summary>
		/// 常駐スキルのセーブデータを読み込む
		/// </summary>
		/// <param name="saveData"></param>
		private void ReadSaveDataSustainSkill(XElement saveData)
		{
			var SustainSkillsData = saveData.Element(SaveCst.SdeSustainSkills);
			
			if(SustainSkillsData == null)
			{
				return;
			}

			foreach(var element in SustainSkillsData.Elements(SaveCst.SdeSkillId))
			{
				// 常駐スキルを活性化する
				ActiveSustainSkill(GeneralUtil.StrToEnum<SkillId>(element.Value));
			}

			/// <summary>
			/// 常駐スキルを活性化する
			/// </summary>
			void ActiveSustainSkill(SkillId skillId)
			{
				// スキルデータを取得する
				var skillBn = SkillMng.GetSkillBean(skillId);

				// スキルインスタンスを生成する
				var type = Type.GetType(SystemCst.ClassNameSpace + skillBn.ClassName);
				var args = new object[] { skillBn, this };
				var skillBo = (ASustainSkillBo) Activator.CreateInstance(type, args);

				// スキルの活性状態を切り替える
				skillBo.SwitchActivation();
			}
		}


		/// <summary>
		/// ダメージを受けたログを取得する
		/// </summary>
		public string GetLogTakeDmg(UnitBo attacker, int hpDmg, int spDmg)
		{
			var sb = new StringBuilder();

			// メッセージを出す
			if(hpDmg > 0)
			{
				if(spDmg > 0)
				{
					// HP、WPダメージ
					// SPダメージのみ
					var prms = new List<object>() { hpDmg, spDmg };
					sb.Append(SysTxtMng.Replace(BattleMng.GetMiscTxt(BattleMiscTxt.TakeDmgHpWp), this, prms));
				}
				else
				{
					// HPダメージのみ
					var prms = new List<object>() { hpDmg };
					sb.Append(SysTxtMng.Replace(BattleMng.GetMiscTxt(BattleMiscTxt.TakeDmg), this, prms));
				}
			}
			else if(spDmg > 0)
			{
				// WPダメージのみ
				var prms = new List<object>() { spDmg };
				sb.Append(SysTxtMng.Replace(BattleMng.GetMiscTxt(BattleMiscTxt.TakeDmgWp), this, prms));
			}
			else
			{
				// ダメージを与えられない
				if(attacker == null)
				{
					var prms = new List<object>() { attacker.GetName(), GetName() };
					sb.Append(SysTxtMng.ReplaceParams(BattleMng.GetMiscTxt(BattleMiscTxt.NoDmg), prms));
				}
				else
				{
					sb.Append(SysTxtMng.ReplaceSubject(BattleMng.GetMiscTxt(BattleMiscTxt.NoDmg2), this));
				}
			}

			return sb.ToString();
		}


		/// <summary>
		/// 一定時間待機する
		/// </summary>
		private void Delay(Action action = null)
		{
			// 0秒でもDelayedCall()は1フレーム遅れるため
			if(IsOnDelay)
			{
				DOVirtual.DelayedCall(delaySecond, () =>
				{
					DelaySecond = 0;
					action?.Invoke();
				});
			}
			else
			{
				action?.Invoke();
			}
		}


		/// <summary>
		/// 耕作を試みる
		/// </summary>
		public bool TryCultivating((int, int) targetPos, List<(EquipPartBo, ItemBo)> weapons)
		{
			ItemBo hoe = null;
			bool isRight = true;

			// 鍬を抽出する
			foreach(var val in weapons)
			{
				if(val.Item2.WeaponBn.HasEfct(WeaponEfct.Hoe))
				{
					hoe = val.Item2;
					isRight = val.Item1.partOrder == PartOrder.First;

					break;
				}
			}

			if(hoe != null)
			{
				// 武器を振った時の処理をアニメイベントに登録
				animEvt.SubscribeOnSwing(() =>
				{
					// 武器を振るSEを再生する
					SoundMng.PlaySe(this, hoe.WeaponBn.RandomSwingSe());
				});

				// ヒットする時の処理をアニメイベントに登録
				animEvt.SubscribeOnHit(() =>
				{
					// 耕作SE
					var seId = GeneralUtil.Random(SeId.Cultivate, SeId.Cultivate2, SeId.Cultivate3);
					SoundMng.PlaySe(hoe, seId);

					// 土煙
					var vfxObj = EffectMng.SpawnVfx(VfxId.DustCloud, FieldMng.ToWorldCoordCenter(targetPos), false);

					// 地形を畑にする
					var girdBo = FieldMng.FieldBo.GetGrid(targetPos);
					girdBo.ChangeTile(TileMng.GetTileBean(TileCst.IdFiled));
				});

				// アニメーション終了時の処理
				var animFinshed = new Subject<Unit>();
				animFinshed.Subscribe(_ =>
				{
					// ターン終了
					StartEndingTurn();
				});

				// 採掘アニメーション
				PlayAnimTrigger(isRight ? AnimationCst.PrmtIsAtkChopVR : AnimationCst.PrmtIsAtkChopVL, animFinshed);

				return true;
			}

			return false;
		}


		/// <summary>
		/// 採掘を試みる
		/// </summary>
		public bool TryMining(ItemBo targetItem, List<(EquipPartBo, ItemBo)> weapons)
		{
			// 標的が岩
			var rockMdl = targetItem.GetModule<RockMdl>();

			if(rockMdl != null)
			{
				ItemBo pickaxe = null;
				int pickaxePow = 0;
				bool isRight = true;

				// 最も硬いつるはしを抽出する
				foreach(var val in weapons)
				{
					// 採掘特性を持たない武器は処理しない
					if(!val.Item2.WeaponBn.HasEfct(WeaponEfct.Pickaxe))
					{
						continue;
					}

					if(val.Item2.WeaponBn.GetEfctPow(WeaponEfct.Pickaxe) > pickaxePow)
					{
						pickaxe = val.Item2;
						pickaxePow = val.Item2.WeaponBn.GetEfctPow(WeaponEfct.Pickaxe);
						isRight = val.Item1.partOrder == PartOrder.First;
					}
				}

				if(pickaxe != null)
				{
					string stateName = default;

					if(rockMdl.IsBreakable(pickaxePow))
					{
						if(isRight)
						{
							stateName = AnimationCst.PrmtIsAtkChopVR;
						}
						else
						{
							stateName = AnimationCst.PrmtIsAtkChopVL;
						}
					}
					else
					{
						if(isRight)
						{
							stateName = AnimationCst.PrmtIsAtkChopVPR;
						}
						else
						{
							stateName = AnimationCst.PrmtIsAtkChopVPL;
						}
					}

					// 採掘アニメーション
					PlayAnimTrigger(stateName);

					// 武器を振った時の処理をアニメイベントに登録
					animEvt.SubscribeOnSwing(() =>
					{
						// 武器を振るSEを再生する
						SoundMng.PlaySe(this, pickaxe.WeaponBn.RandomSwingSe());
					});

					// ヒットする時の処理をアニメイベントに登録
					animEvt.SubscribeOnHit(() =>
					{
						// ダメージ情報
						var atkBo = BattleMng.CalcAtkBo(this, pickaxe);
						var dmgBo = BattleMng.CalcDmgBo(atkBo, targetItem, false, DmgType.Melee);

						// 岩にヒットした時の処理
						rockMdl.OnMining(dmgBo);
					});

					return true;
				}
			}

			return false;
		}


		/// <summary>
		/// 伐採を試みる
		/// </summary>
		public bool TryLogging(ItemBo tree, List<(EquipPartBo, ItemBo)> weapons)
		{
			// 木でないなら伐採不可
			if(!tree.ItemBn.TreeFlg)
			{
				return false;
			}

			ItemBo axe = null;
			bool isRight = true;

			foreach(var val in weapons)
			{
				// 伐採特性を持つか
				if(val.Item2.WeaponBn.HasEfct(WeaponEfct.Lumberjack))
				{
					axe = val.Item2;
					isRight = val.Item1.partOrder == PartOrder.First;

					break;
				}
			}

			// 伐採特性の武器を持たないなら処理しない
			if(axe == null)
			{
				return false;
			}

			// 伐採アニメーション
			PlayAnimTrigger(isRight ? AnimationCst.PrmtIsAtkChopHR : AnimationCst.PrmtIsAtkChopHL);

			// 武器を振った時の処理をアニメイベントに登録
			animEvt.SubscribeOnSwing(() =>
			{
				// 武器を振るSEを再生する
				SoundMng.PlaySe(this, axe.WeaponBn.RandomSwingSe());
			});

			// ヒットする時の処理をアニメイベントに登録
			animEvt.SubscribeOnHit(() =>
			{
				// ダメージ情報
				var atkBo = BattleMng.CalcAtkBo(this, axe);
				var dmgBo = BattleMng.CalcDmgBo(atkBo, tree, false, DmgType.Melee);

				// 木を切るSE
				SoundMng.PlaySe(axe, SeId.WoodChop);

				// 木にヒットした時の処理
				tree.GetModule<TreeMdl>().OnLogging(dmgBo, isRight);
			});

			return true;
		}


		/// <summary>
		/// レベルアップする
		/// </summary>
		/// <param name="reachingLvl">到達レベル</param>
		public void LvlUp(int reachingLvl)
		{
			if(Lvl >= reachingLvl)
			{
				return;
			}

			for(int i = Lvl + 1; i <= reachingLvl; i++)
			{
				Lvl++;
			}

			// レベルアップログ
			var prms = new List<object>() { reachingLvl };
			MsgBoardMng.Log(SysTxtMng.ReplaceParams(SysTxtMng.GetTxt(SysTxtId.LvlUp), prms));
		}


		/// <summary>
		/// スキルの発動を試みる
		/// </summary>
		public bool TryUseSkill(SkillId skillId, AAction action)
		{
			// スキルデータを取得する
			var skillBn = SkillMng.GetSkillBean(skillId);

			// スキルインスタンスを生成する
			var type = Type.GetType(SystemCst.ClassNameSpace + skillBn.ClassName);
			var args = new object[] { skillBn, action };
			var skillBo = (SkillBo) Activator.CreateInstance(type, args);

			// 発動する
			return skillBo.Active();
		}


		/// <summary>
		/// 指定スキルを持つかを調べる
		/// </summary>
		/// <returns>true: 持つ</returns>
		public bool HasSkill(SkillId skillId)
		{
			foreach(var pair in SkillList)
			{
				if(pair.Item1 == skillId)
				{
					return true;
				}
			}

			return false;
		}


		/// <summary>
		/// AIを初期化する
		/// </summary>
		private void InitUnitAi()
		{
			Type type = default;

			if(UnitBn.AiClass == SystemCst.NoneValue || string.IsNullOrEmpty(UnitBn.AiClass))
			{
				type = typeof(UnitAi);
			}
			else
			{
				type = Type.GetType(SystemCst.ClassNameSpace + UnitBn.AiClass);
			}

			Ai = (UnitAi) Activator.CreateInstance(type, this);
		}


		/// <summary>
		/// 全アイテムを消去する
		/// </summary>
		public void ClearAllItems()
		{
			inventory.Clear();
			equipmentDict.Clear();
		}


		/// <summary>
		/// 放火可能なアイテムを装備しているか
		/// </summary>
		/// <returns>true: 装備している</returns>
		public bool HasSetFireableEquipment()
		{
			for(int i = 0; i < equipmentList.Count; i++)
			{
				if(equipmentList[i].IsSetFireable())
				{
					return true;
				}
			}

			return false;
		}


		/// <summary>
		/// 全回復
		/// </summary>
		public void FullRecovery()
		{
			UnitBn.SetHitP(UnitBn.MaxHitP);

			// 状態異常リストをクリアする
			var tmpSeBoList = new List<StatusEffectBo>(statusEffectBoList);

			for(int i = 0; i < tmpSeBoList.Count; i++)
			{
				RemoveStatusEffect(tmpSeBoList[i]);
			}

			isDead = false;
		}


		/// <summary>
		/// 正面の座標に進む時の符号を取得する
		/// </summary>
		/// <returns></returns>
		public (int, int) GetFrontPosSigns()
		{
			int xSign = 0;
			int ySign = 0;

			switch(Dir)
			{
				case Direction.Up:
					ySign = 1;
					break;

				case Direction.UpRight:
					xSign = 1;
					ySign = 1;
					break;

				case Direction.Right:
					xSign = 1;
					break;

				case Direction.DownRight:
					xSign = 1;
					ySign = -1;
					break;

				case Direction.Down:
					ySign = -1;
					break;

				case Direction.DownLeft:
					xSign = -1;
					ySign = -1;
					break;

				case Direction.Left:
					xSign = -1;
					break;

				case Direction.UpLeft:
					xSign = -1;
					ySign = 1;
					break;
			}

			return (xSign, ySign);
		}
	}
}