// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAutomaticCheckRuntimeState.cs
// Description: 1クリック自動確認の実行・レポート・復元状態を保持する
// Reason: 巨大サービスから同一ライフサイクルの可変状態を責務単位で分離するため

namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundAutomaticCheckRuntimeState
{
    public TitleBackgroundAutomaticCheckState State { get; set; } =
        TitleBackgroundAutomaticCheckState.Idle;

    public bool Requested { get; set; }

    public DateTimeOffset? CompletionDueAt { get; set; }

    public DateTimeOffset? LoginObservedAt { get; set; }

    public string Status { get; set; } = "自動確認は未開始です。";

    public string LastReport { get; set; } = string.Empty;

    public string PendingClipboardText { get; set; } = string.Empty;

    public bool ReportAvailabilityInitialized { get; set; }

    public bool ReportAvailable { get; set; }

    public TitleBackgroundAutomaticCheckSettingsSnapshot? SettingsSnapshot { get; set; }

    public string RunId { get; set; } = string.Empty;

    public bool SettingsRestored { get; set; } = true;

    // OneClick 実機確認 run の間だけ true。config は書かず、この bool で placement proof を owner にする
    // （PR #7 根本修正: config フリップ方式の廃止）。run 終了・失敗・cancel・dispose で false。
    public bool PlacementProofArmed { get; set; }

    // login 時に freeze した completed-run proof snapshot。以後 report はここから出す
    // （live runtime が reset/restore で消えても completed report が正しい）。
    public TitleBackgroundCharaSelectPlacementProofSnapshot? CompletedRunProof { get; set; }

    // proof 完了後、設定復元→promotion→reload の結果を report へ引き継ぐ。
    public bool PlacementPromotionEligible { get; set; }
    public bool PlacementPromotionPersisted { get; set; }
    public string PlacementPromotionStatus { get; set; } = "not-evaluated";
    public string PlacementPromotionReason { get; set; } = "not-evaluated";

    public void ResetPlacementPromotion()
    {
        PlacementPromotionEligible = false;
        PlacementPromotionPersisted = false;
        PlacementPromotionStatus = "not-evaluated";
        PlacementPromotionReason = "not-evaluated";
    }
}
