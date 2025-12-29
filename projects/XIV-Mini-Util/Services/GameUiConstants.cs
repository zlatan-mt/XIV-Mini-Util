// Path: projects/XIV-Mini-Util/Services/GameUiConstants.cs
// Description: ゲームUI操作に必要なアドオン名とコールバック定数を管理する
// Reason: パッチによる変更点を一箇所で更新できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/GameUiService.cs, projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Services/DesynthService.cs
namespace XivMiniUtil.Services;

public static class GameUiConstants
{
    // TODO: パッチごとの実値で更新する
    public const string MateriaExtractAddonName = "MateriaExtract";
    public const string DesynthAddonName = "SalvageDialog";

    // 未確定のため -1 を使い、実行前に検証する
    public const int MateriaExtractConfirmCallbackId = -1;
    public const int DesynthConfirmCallbackId = -1;
}
