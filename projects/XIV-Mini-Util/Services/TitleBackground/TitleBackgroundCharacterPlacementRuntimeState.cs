// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharacterPlacementRuntimeState.cs
// Description: pre-loginキャラDrawObject観測とCharaSelectキャラ配置記録のセッション限定状態を保持する
// Reason: 巨大サービスから同一ライフサイクルの可変状態を責務単位で分離するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

// pre-loginキャラDrawObject観測とCharaSelectキャラ配置記録のセッション限定状態（config非保存）。
internal sealed class TitleBackgroundCharacterPlacementRuntimeState
{
    public Vector3? LastPreLoginCharacterDrawPosition { get; set; }

    public float LastPreLoginCharacterDrawRotation { get; set; }

    public int PreLoginCharacterDrawObservedCount { get; set; }

    public int CharaSelectCharacterPlacementCount { get; set; }

    public string CharaSelectCharacterPlacementLastError { get; set; } = "none";

    public Vector3? LastCharaSelectCharacterPlacementTarget { get; set; }

    public string LastCharaSelectCharacterPlacementSource { get; set; } = "none";

    // 直近の配置で使ったアンカーの frame（地面 provenance 判定に使う）。camera-focus 由来は Unknown。
    public string LastCharaSelectCharacterPlacementAnchorFrame { get; set; } = TitleBackgroundCharaSelectAnchorFrame.Unknown;
}
