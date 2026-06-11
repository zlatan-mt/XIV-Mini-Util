// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundNativeDelegates.cs
// Description: TitleBackground native hook delegate と ABI 用構造体を定義する
// Reason: unmanaged hook 型を TitleScreenBackgroundService の本体ロジックから分離するため
using System.Runtime.InteropServices;

namespace XivMiniUtil.Services.TitleBackground;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate int CreateSceneDelegate(byte* territoryPath, uint territoryId, nint p3, uint layerFilterKey, nint festivals, int p6, uint contentFinderConditionId);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate byte LobbyUpdateDelegate(GameLobbyType mapId, int time);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void LoadLobbySceneDelegate(GameLobbyType mapId);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void LobbySceneLoadedDelegate(nint thisPtr);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
// UNVERIFIED ABI - DO NOT ENABLE BY DEFAULT. Phase 1 does not create this hook.
internal unsafe delegate nint LobbyCameraFixOnDelegate(nint self, float* cameraPos, float* focusPos, float fovY);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal unsafe delegate float CalculateLobbyCameraLookAtYDelegate(
    nint self,
    float distance,
    CurvePoint* lowPoint,
    CurvePoint* midPoint,
    CurvePoint* highPoint);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SetCameraCurveMidPointDelegate(nint self, float value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void CalculateCameraCurveLowAndHighPointDelegate(nint self, float value);

[StructLayout(LayoutKind.Sequential)]
internal struct CurvePoint
{
    public float X;
    public float Y;
}
