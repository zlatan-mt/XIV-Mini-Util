// Path: projects/XIV-Mini-Util/Services/GameUiConstants.cs
// Description: ゲームUI操作に必要なアドオン名とコールバック定数を管理する
// Reason: パッチによる変更点を一箇所で更新できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/GameUiService.cs, projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Services/DesynthService.cs
namespace XivMiniUtil.Services.Common;

public static class GameUiConstants
{
    // TODO: パッチごとの実値で更新する
    public const string MaterializeAddonName = "Materialize";
    public const string MaterializeDialogAddonName = "MaterializeDialog";
    public const string SalvageDialogAddonName = "SalvageDialog";
    public const string SalvageAutoDialogAddonName = "SalvageAutoDialog";
    public const string SalvageResultAddonName = "SalvageResult";
    public const string SalvageItemSelectorAddonName = "SalvageItemSelector";

    // Materialize: 既存プラグインの参照値（パッチで変化する可能性あり）
    public const int MaterializeSelectCallbackValue0 = 2;
    public const uint MaterializeSelectCallbackValue1 = 0;
    public const int MaterializeSelectCallbackPrimaryCount = 2;
    public const int MaterializeSelectCallbackFallbackCount = 1;

    // SalvageDialog: 確認ダイアログのYes操作
    public const int SalvageDialogConfirmValue0 = 0;
    public const bool SalvageDialogConfirmValue1 = false;

    // SalvageItemSelector: アイテム選択コールバック（実装は次ステップ）
    public const int SalvageItemSelectValue0 = 12;
}
