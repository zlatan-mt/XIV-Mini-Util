// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectEngineOwner.cs
// Description: Character Select エンジンの「今どの writer が owner か」を 1 か所で判定する小さな pure resolver。
// Reason: PR #7 根本修正。OneClick の placement 所有権を config フリップ（V2Enabled=false/PlacementEnabled=true の
//         一時保存）で表現するのをやめ、run-scoped runtime bool（AutomaticPlacementProofArmed）で表す。
//         各 writer が個別に config を推測せず、排他判定をこの resolver の結果へ寄せる。
namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundCharaSelectEngineOwner
{
    // override OFF、または何も有効でない。legacy camera-maintain 経路が動ける唯一の状態。
    None,
    // 恒久設定の V2 framing。
    V2,
    // 恒久設定の TitleEdit-informed placement path。
    Placement,
    // OneClick 実機確認 run 中だけの run-scoped placement proof。
    // config 上は V2Enabled=true のままでも、実 owner はこれ（V2 writer は動いてはいけない）。
    PlacementProof,
}

internal static class TitleBackgroundCharaSelectEngineOwnerLogic
{
    // owner =
    //   automatic OneClick placement proof armed ? PlacementProof
    //   : persistent placement enabled           ? Placement
    //   : V2 enabled                             ? V2
    //   : None
    // ただし override OFF ではいずれの新エンジンも owner にならない（既存 IsV2Active / IsCharaSelectPlacementActive
    // が override を前提にしているのと同じ）。
    public static TitleBackgroundCharaSelectEngineOwner Resolve(
        bool overrideEnabled,
        bool automaticPlacementProofArmed,
        bool persistentPlacementEnabled,
        bool v2Enabled)
    {
        if (!overrideEnabled)
        {
            return TitleBackgroundCharaSelectEngineOwner.None;
        }

        if (automaticPlacementProofArmed)
        {
            return TitleBackgroundCharaSelectEngineOwner.PlacementProof;
        }

        if (persistentPlacementEnabled)
        {
            return TitleBackgroundCharaSelectEngineOwner.Placement;
        }

        if (v2Enabled)
        {
            return TitleBackgroundCharaSelectEngineOwner.V2;
        }

        return TitleBackgroundCharaSelectEngineOwner.None;
    }

    // placement path（proof or persistent）が owner か。IsCharaSelectPlacementActive の実体。
    public static bool IsPlacementOwner(TitleBackgroundCharaSelectEngineOwner owner)
        => owner is TitleBackgroundCharaSelectEngineOwner.PlacementProof
            or TitleBackgroundCharaSelectEngineOwner.Placement;

    // V2 が owner か。IsV2Active の実体。proof armed 中は false（config V2Enabled に関わらず）。
    public static bool IsV2Owner(TitleBackgroundCharaSelectEngineOwner owner)
        => owner == TitleBackgroundCharaSelectEngineOwner.V2;

    // 新 CharaSelect エンジン（V2 or placement）が owner か。legacy camera / Phase2G / FixOn override /
    // 旧 per-frame placement の排他の single source of truth。
    public static bool IsNewEngineOwner(TitleBackgroundCharaSelectEngineOwner owner)
        => IsPlacementOwner(owner) || IsV2Owner(owner);

    // proof arm によって V2 の保存設定が抑止されているか（report の automaticRun.v2Suppressed）。
    public static bool IsV2SuppressedByProof(
        TitleBackgroundCharaSelectEngineOwner owner,
        bool v2Enabled)
        => owner == TitleBackgroundCharaSelectEngineOwner.PlacementProof && v2Enabled;

    public static string Describe(TitleBackgroundCharaSelectEngineOwner owner)
        => owner switch
        {
            TitleBackgroundCharaSelectEngineOwner.PlacementProof => "placement-proof",
            TitleBackgroundCharaSelectEngineOwner.Placement => "placement",
            TitleBackgroundCharaSelectEngineOwner.V2 => "v2",
            _ => "none",
        };
}
