// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectPresetSelectionLogic.cs
// Description: 通常画面の curated 背景セレクタが扱う「見せてよい選択肢」の並びと、UI 選択 <-> registry candidate id の解決。
// Reason: AGENTS.md 恒久契約（通常画面に生 candidate / probe / 診断を出さない）を守りつつ、
//         Simple UI とテストが同じ curated 定義を1箇所から引くため。純粋ロジックのみ（ImGui/Config 非依存）。
namespace XivMiniUtil.Services.TitleBackground;

// 通常画面 Combo の1項目。CandidateId が空文字なら「OFF」を表す。
internal readonly record struct TitleBackgroundCharaSelectPresetChoice(
    string CandidateId,
    string DisplayLabel,
    bool Experimental);

internal static class TitleBackgroundCharaSelectPresetSelectionLogic
{
    // OFF は常に先頭・固定。CandidateId は空文字で表現する。
    public const string OffCandidateId = "";

    // 通常画面に出してよい curated candidate の許可リスト（registry の全 candidate を無条件に出さない）。
    // ここに載っていない registry entry（manual スロット等）は通常画面から見えない。
    // 実験中フラグは VerifiedInGame=false の curated candidate に付ける。
    private static readonly IReadOnlyList<string> CuratedCandidateOrder =
    [
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId, // custom:n4f4  イル・メグ
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId, // エルピスの花畑
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId, // FRU クリア後ステージ
    ];

    // 通常画面 Combo に出す順序付き選択肢（先頭は必ず OFF）。
    // curated candidate だけを出し、未検証 candidate はラベル末尾の " [実験中]" だけで区別する（UI は増やさない）。
    public static IReadOnlyList<TitleBackgroundCharaSelectPresetChoice> BuildChoices()
    {
        var choices = new List<TitleBackgroundCharaSelectPresetChoice>
        {
            new(OffCandidateId, "OFF", false),
        };
        foreach (var id in CuratedCandidateOrder)
        {
            if (!TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(id, out var candidate))
            {
                continue;
            }

            var experimental = !candidate.VerifiedInGame;
            choices.Add(new(id, JapaneseLabelFor(id) + (experimental ? " [実験中]" : string.Empty), experimental));
        }

        return choices;
    }

    // 通常画面に出してよい curated candidate かどうか（registry 内でも manual/unknown は false）。
    public static bool IsCuratedCandidateId(string? candidateId)
    {
        var normalized = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(candidateId);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        foreach (var id in CuratedCandidateOrder)
        {
            if (string.Equals(id, normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // 選択中の candidate id（Configuration.TitleBackgroundCharacterSelectOverrideCandidateId）と
    // override 有効フラグから、Combo の現在選択 index を返す。未一致/OFF は 0（OFF）。
    public static int ResolveSelectedIndex(bool overrideEnabled, string? selectedCandidateId)
    {
        if (!overrideEnabled)
        {
            return 0;
        }

        var normalized = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(selectedCandidateId);
        var choices = BuildChoices();
        for (var i = 0; i < choices.Count; i++)
        {
            if (string.Equals(choices[i].CandidateId, normalized, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    // Combo で選ばれた index を candidate id へ。範囲外は OFF。
    public static string ResolveCandidateIdForIndex(int index)
    {
        var choices = BuildChoices();
        return index >= 0 && index < choices.Count ? choices[index].CandidateId : OffCandidateId;
    }

    // 選択中 index に対応する Combo プレビュー文言。
    public static string ResolvePreviewLabel(bool overrideEnabled, string? selectedCandidateId)
    {
        var choices = BuildChoices();
        var index = ResolveSelectedIndex(overrideEnabled, selectedCandidateId);
        return index >= 0 && index < choices.Count ? choices[index].DisplayLabel : "OFF";
    }

    // id -> 通常画面用の日本語ラベル（英語内部名 DisplayName を出さないための対応表）。
    private static string JapaneseLabelFor(string candidateId)
    {
        return candidateId switch
        {
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId => "イル・メグ",
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId => "エルピスの花畑",
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId => "FRU クリア後ステージ",
            _ => "カスタム背景",
        };
    }
}
