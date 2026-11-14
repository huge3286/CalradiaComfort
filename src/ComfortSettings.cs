using MCM.Abstractions.Base.Global;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;

namespace StorySkip
{
    public sealed class ComfortSettings : AttributeGlobalSettings<ComfortSettings>
    {
        public override string Id => "CalradiaComfort";
        public override string DisplayName => ComfortBranding.DisplayName;
        public override string FolderName => "CalradiaComfort";
        public override string FormatType => "json";

        [SettingPropertyBool("跳过开局主线与战帆救援", Order = 0, RequireRestart = false,
            HintText = "新建战役时生效。直接获得完整龙旗、家族成员，并保留交付龙旗或自立的后续选择。")]
        [SettingPropertyGroup("开局功能", GroupOrder = 0)]
        public bool SkipOpeningStory { get; set; } = true;

        [SettingPropertyBool("启用 Steam 成就", Order = 0, RequireRestart = false,
            HintText = "允许使用模组或开启作弊模式的战役与沙盒正常检查 Steam 成就。仍需满足对应成就的原版条件。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool EnableAchievements { get; set; } = true;

        [SettingPropertyBool("同时获得两边 Perk", Order = 1, RequireRestart = false,
            HintText = "仅玩家家族的主角、家族成员和已加入家族的同伴生效。自动领取所有技能等级达标的两边 Perk，包含战帆技能；读取存档时补齐。关闭后停止自动领取，已获得的 Perk 保留。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool BothPerks { get; set; } = true;

        [SettingPropertyBool("自动满属性、专精与技能", Order = 2, RequireRestart = false,
            HintText = "仅玩家家族的主角、成年成员和同伴生效。属性补到原版上限 10、专精固定为原版上限 5，技能达到实际成长极限，包含战帆技能。修正旧档超额专精及超出极限的技能；无需手动加点，待分配点数清零。关闭后恢复原版成长。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool LearningLimitSkills { get; set; } = true;

        [SettingPropertyBool("士兵共享经验槽", Order = 3, RequireRestart = false,
            HintText = "主角当前部队的普通士兵经验存入一个共享槽，可用于升级任意兵种。金币、马匹及其他升级条件保持原版要求。经验槽随存档保存；关闭后保留余额，新经验按原版分配。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool SharedTroopExperience { get; set; } = true;

        [SettingPropertyBool("分类一键招募", Order = 4, RequireRestart = false,
            HintText = "在招募页面显示招募普通兵和招募贵族兵按钮，分别选取当前页面可招募的对应兵系。贵族兵包含各国贵族兵系。只选取关系已解锁、金币可支付且部队有空位的士兵；按原价结算，点击完成后招募，取消可撤销。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool QuickRecruitment { get; set; } = true;

        [SettingPropertyBool("一键升满建筑", Order = 5, RequireRestart = false,
            HintText = "仅玩家家族的城镇和城堡生效。在管理页面点击“一键升满建筑”，将当前领地的全部建筑升到原版三级，点击即生效。建筑效果及完工事件按原版处理。住房、灌溉、庆典等日常项目保持原状。关闭后隐藏按钮，已完成的等级保留。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool InstantConstruction { get; set; } = true;

        [SettingPropertyBool("士兵一键升到最高级", Order = 6, RequireRestart = false,
            HintText = "需要开启士兵共享经验槽。在主角部队的升级弹窗中选择该兵系可达的最终兵种，逐级支付原版经验、金币、马匹并检查 Perk 条件。只升级能支付完整路线的健康士兵，其余保留原兵种。点击完成结算，取消可撤销。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool UltimateTroopUpgrade { get; set; } = true;

        [SettingPropertyBool("家族女性联姻改为入赘", Order = 7, RequireRestart = false,
            HintText = "玩家家族女性之后结婚时留在本家族，男方加入玩家家族，包括姐妹、女儿及女性主角。原版联姻资格与费用保留；男方是族长且无合格继承者时不允许该婚姻。其他家族不受影响，不追溯此前婚姻；关闭后恢复之后婚姻的原版归属规则。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool MatrilinealMarriage { get; set; } = true;

        [SettingPropertyBool("主角多配偶", Order = 8, RequireRestart = false,
            HintText = "仅主角可与多名符合原版婚姻条件的未婚成年角色结婚。保留原版求婚、说服、婚约和费用；原配偶及子女留在家族。关闭后停止新增多配偶，已成立的婚姻保留。其他家族成员仍按原版单配偶。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool PluralMarriage { get; set; } = true;

        [SettingPropertyBool("俘虏对话当场联姻", Order = 9, RequireRestart = false,
            HintText = "与主角部队中的未婚成年异性贵族俘虏对话，直接打开含“联姻”和“释放对方”的原版交易界面。成交时先释放，随后由自由角色依据关系和吸引力决定接受婚约；拒绝也保留自由，不扣聘金、不结婚。取消尚未提交的交易不改变角色和金币。婚后家族归属遵循联姻与入赘设置，生育遵循原版条件；仍可单独无条件释放。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool ReleasedCaptiveCourtship { get; set; } = true;

        [SettingPropertyBool("丢弃物品换金币", Order = 10, RequireRestart = false,
            HintText = "确认丢弃背包物品、或完成战利品页面时，将最终丢弃的物品按基础估值兑换金币。估值包含品质词条，不使用城镇市场价；物品正常移除。取消不兑换，商店出售、仓库转移和捐赠不参与，任务物品不能兑换。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool DiscardItemGold { get; set; } = true;

        [SettingPropertyInteger("丢弃兑换比例（%）", 0, 100, Order = 11, RequireRestart = false,
            HintText = "物品基础估值乘以此百分比，默认 30%。整批计算后舍去不足 1 第纳尔的部分。0% 不发金币；100% 按完整基础估值兑换。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public int DiscardItemGoldPercent { get; set; } = 30;

        [SettingPropertyBool("百科角色实时位置与状态", Order = 12, RequireRestart = false,
            HintText = "百科角色页面显示当前聚落、所在部队及被俘状态，未见过的角色也可查询。位置读取当前世界数据；逃亡、释放恢复中或尚未出现而没有地图实体的角色会明确说明，死亡角色不显示旧位置。关闭后隐藏新增信息。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool HeroLiveLocation { get; set; } = true;

        [SettingPropertyBool("自定义家族部队数量上限", Order = 13, RequireRestart = false,
            HintText = "仅调整玩家家族的作战部队数量上限，默认 20 支。仍需符合原版领队资格并支付创建和维持部队的费用，不自动生成部队。关闭后使用原版家族等级与 Perk 计算。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool ClanPartyLimit { get; set; } = true;

        [SettingPropertyInteger("家族部队数量上限", 1, 100, Order = 14, RequireRestart = false,
            HintText = "玩家家族可拥有的作战部队总数上限，含主角部队，默认 20，范围 1 至 100。保存设置后重新打开家族部队页面查看；降低数值不会主动解散已有部队。商队与驻军按原版各自规则。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public int ClanPartyLimitValue { get; set; } = 20;

        [SettingPropertyBool("己方大地图固定加速", Order = 15, RequireRestart = false,
            HintText = "玩家所属王国的全部大地图队伍获得固定速度加成，包括领主部队、商队、村民和巡逻队；独立时仅玩家家族所属队伍生效。陆地与海上均生效，军团只计一次，速度明细显示“风之气息”。其他王国与敌军不受影响。保存设置后随下一次速度计算更新，无需新开档。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool AlliedPartySpeed { get; set; } = true;

        [SettingPropertyFloatingInteger("大地图速度固定加成", 0f, 10f, "0.0", Order = 16, RequireRestart = false,
            HintText = "在原版最终移动速度上直接增加此数值，默认 1.0，范围 0 至 10，可设置小数。0 不增加速度；此项是固定数值，不是百分比。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public float AlliedPartySpeedBonus { get; set; } = 1f;

        [SettingPropertyBool("配偶对话立即怀孕", Order = 17, RequireRestart = false,
            HintText = "与成年配偶对话时显示“我们来开一局吧”。选择后立即进入原版怀孕流程，按正常孕期生育，兼容主角多配偶。已怀孕时不能重复触发；被俘双方和未开启生育的战役不能使用。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool InstantPregnancy { get; set; } = true;

        [SettingPropertyBool("主角破防、碎盾与一击破城门", Order = 18, RequireRestart = false,
            HintText = "仅主角战斗攻击生效：武器格挡必定突破，盾牌格挡一击碎盾，攻击攻城城门一击摧毁。友军和其他角色不获得能力；踢击、盾击等替代攻击保持原版。关闭后恢复原版后续攻击，已经损毁的盾牌和城门不恢复。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool OverwhelmingAttack { get; set; } = true;

        [SettingPropertyBool("攻城立即进攻", Order = 19, RequireRestart = false,
            HintText = "玩家主持围城时立即完成攻城营地准备，直接使用原版率兵进攻。其他领主主持的围城不变；攻城器械仍按原版建造，不自动开战。已有围城也可开启使用。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool InstantSiegeAssault { get; set; } = true;

        [SettingPropertyBool("自定义家族同伴数量上限", Order = 20, RequireRestart = false,
            HintText = "仅玩家家族生效，同伴总数上限默认 20。仍需正常招募并支付费用，不自动生成同伴。关闭后恢复原版家族等级和 Perk 计算；不主动移除已招募同伴。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool ClanCompanionLimit { get; set; } = true;

        [SettingPropertyInteger("家族同伴数量上限", 1, 100, Order = 21, RequireRestart = false,
            HintText = "玩家家族可招募的同伴总数上限，默认 20，范围 1 至 100。此数值是最终上限，不额外叠加原版等级或 Perk 名额；亲属和配偶按原版不占同伴名额。保存设置后重新打开家族页面查看。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public int ClanCompanionLimitValue { get; set; } = 20;

        [SettingPropertyBool("关系变化仅显示文字日志", Order = 22, RequireRestart = false,
            HintText = "将原版关系增加、降低提示改为左下角文字日志，取消顶部横幅、头像和提示音。关系数值与事件照常处理，原本隐藏的关系变化仍不提示；其他重要通知保留。关闭后恢复之后的原版关系横幅。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool QuietRelationNotifications { get; set; } = true;

        [SettingPropertyBool("酒馆一键卖出家族俘虏领主", Order = 23, RequireRestart = false,
            HintText = "酒馆区新增卖出所有俘虏领主选项，包含主角、玩家家族其他部队及家族城镇／城堡地牢。按主角原版酒馆赎金结算，钱归主角，领主正常获释。普通俘虏、已锁定及不可转移角色保留；其他家族与战斗中的来源不参与。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool SellClanLordPrisoners { get; set; } = true;

        [SettingPropertyBool("读取存档补充便携火罐", Order = 24, RequireRestart = false,
            HintText = "新建或载入战役后，主角背包及武器栏没有便携火罐时补一组，基础容量 10。手动装备，使用原版投石的投掷和补投动作，支持步行、骑马及持盾使用，保留火罐燃烧和范围伤害。使用普通投掷武器流程，单独禁止出售、丢弃和丢弃换钱。关闭后停止补发，已有火罐保留。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool PortableFirePots { get; set; } = true;

        [SettingPropertyBool("资本利得", Order = 25, RequireRestart = false,
            HintText = "主角持有金币按游戏内时间逐日复利，年化收益默认 15%，不足一金币的收益累计并随存档保存。只在经过游戏日时结算，不因读档或现实时间发钱；使用期间的花费和收入影响之后本金。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool CapitalGains { get; set; } = true;

        [SettingPropertyFloatingInteger("资本利得年化收益（%）", 0f, 100f, "0.0", Order = 26, RequireRestart = false,
            HintText = "默认 15%。按游戏实际每年天数换算日复利，使本金不变时一年后约为 1.15 倍。仅主角钱包参与，其他英雄、城镇和商队不参与。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public float CapitalGainsAnnualPercent { get; set; } = 15f;

        [SettingPropertyBool("超额驻军转为村庄户数", Order = 27, RequireRestart = false,
            HintText = "仅玩家家族城镇和城堡：每日逃兵检查前，将超过驻军人数上限的普通士兵退役，每人转为直属村庄 1 户，平均分配、余数优先给户数较少的村庄。低阶兵优先，同阶按等级排序；英雄不参与。围城、战斗中或没有直属村庄时不转换。工资及士气逃兵仍按原版规则。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool GarrisonResettlement { get; set; } = true;

        [SettingPropertyBool("家族部队优先救援与攻城", Order = 28, RequireRestart = false,
            HintText = "仅玩家家族作战部队，主角及主角军团不受控制。最近可用队伍优先救村、解围及保护友军攻城队。无紧急任务时主动拦截附近打得过的敌对领主、劫匪与逃兵，再评估攻城；不主动烧村。预留约三分之一指挥单位，补兵／巡防队可完成补给后参与进攻集结，避免重复蹲城补给。紧急救援可接替尚未围城的任务；已经开始的战斗／围城保持原版流程。任务随存档保存；关闭后恢复后续原版 AI。")]
        [SettingPropertyGroup("游玩功能", GroupOrder = 1)]
        public bool ClanPartyStrategy { get; set; } = true;
    }

    internal static class ComfortFeatures
    {
        internal static bool ConfiguredStorySkip => ComfortSettings.Instance?.SkipOpeningStory ?? true;
        internal static bool AchievementsEnabled => ComfortSettings.Instance?.EnableAchievements ?? true;
        internal static bool BothPerksEnabled => ComfortSettings.Instance?.BothPerks ?? true;
        internal static bool LearningLimitSkillsEnabled => ComfortSettings.Instance?.LearningLimitSkills ?? true;
        internal static bool SharedTroopExperienceEnabled => ComfortSettings.Instance?.SharedTroopExperience ?? true;
        internal static bool QuickRecruitmentEnabled => ComfortSettings.Instance?.QuickRecruitment ?? true;
        internal static bool InstantConstructionEnabled => ComfortSettings.Instance?.InstantConstruction ?? true;
        internal static bool UltimateTroopUpgradeEnabled => ComfortSettings.Instance?.UltimateTroopUpgrade ?? true;
        internal static bool MatrilinealMarriageEnabled => ComfortSettings.Instance?.MatrilinealMarriage ?? true;
        internal static bool PluralMarriageEnabled => ComfortSettings.Instance?.PluralMarriage ?? true;
        internal static bool ReleasedCaptiveCourtshipEnabled => ComfortSettings.Instance?.ReleasedCaptiveCourtship ?? true;
        internal static bool DiscardItemGoldEnabled => ComfortSettings.Instance?.DiscardItemGold ?? true;
        internal static int DiscardItemGoldPercent => System.Math.Max(0, System.Math.Min(100,
            ComfortSettings.Instance?.DiscardItemGoldPercent ?? 30));
        internal static bool HeroLiveLocationEnabled => ComfortSettings.Instance?.HeroLiveLocation ?? true;
        internal static bool ClanPartyLimitEnabled => ComfortSettings.Instance?.ClanPartyLimit ?? true;
        internal static int ClanPartyLimitValue => System.Math.Max(1, System.Math.Min(100,
            ComfortSettings.Instance?.ClanPartyLimitValue ?? 20));
        internal static bool AlliedPartySpeedEnabled => ComfortSettings.Instance?.AlliedPartySpeed ?? true;
        internal static bool InstantPregnancyEnabled => ComfortSettings.Instance?.InstantPregnancy ?? true;
        internal static bool OverwhelmingAttackEnabled => ComfortSettings.Instance?.OverwhelmingAttack ?? true;
        internal static bool InstantSiegeAssaultEnabled => ComfortSettings.Instance?.InstantSiegeAssault ?? true;
        internal static bool ClanCompanionLimitEnabled => ComfortSettings.Instance?.ClanCompanionLimit ?? true;
        internal static bool QuietRelationNotificationsEnabled => ComfortSettings.Instance?.QuietRelationNotifications ?? true;
        internal static bool SellClanLordPrisonersEnabled => ComfortSettings.Instance?.SellClanLordPrisoners ?? true;
        internal static bool PortableFirePotsEnabled => ComfortSettings.Instance?.PortableFirePots ?? true;
        internal static bool CapitalGainsEnabled => ComfortSettings.Instance?.CapitalGains ?? true;
        internal static bool GarrisonResettlementEnabled => ComfortSettings.Instance?.GarrisonResettlement ?? true;
        internal static bool ClanPartyStrategyEnabled => ComfortSettings.Instance?.ClanPartyStrategy ?? true;
        internal static float CapitalGainsAnnualPercent
        {
            get
            {
                float value = ComfortSettings.Instance?.CapitalGainsAnnualPercent ?? 15f;
                return float.IsNaN(value) || float.IsInfinity(value) ? 15f : System.Math.Max(0f, System.Math.Min(100f, value));
            }
        }
        internal static int ClanCompanionLimitValue => System.Math.Max(1, System.Math.Min(100,
            ComfortSettings.Instance?.ClanCompanionLimitValue ?? 20));
        internal static float AlliedPartySpeedBonus
        {
            get
            {
                float value = ComfortSettings.Instance?.AlliedPartySpeedBonus ?? 1f;
                return float.IsNaN(value) || float.IsInfinity(value)
                    ? 1f
                    : System.Math.Max(0f, System.Math.Min(10f, value));
            }
        }
    }
}
