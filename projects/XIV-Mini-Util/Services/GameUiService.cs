// Path: projects/XIV-Mini-Util/Services/GameUiService.cs
// Description: DalamudのGameGuiを通じてアドオン操作を行う
// Reason: UI操作の責務をサービスに分離して保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/GameUiConstants.cs, projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Services/DesynthService.cs
using Dalamud.Game.Gui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivMiniUtil.Services;

public sealed class GameUiService
{
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _pluginLog;

    public GameUiService(IGameGui gameGui, IPluginLog pluginLog)
    {
        _gameGui = gameGui;
        _pluginLog = pluginLog;
    }

    public unsafe bool IsAddonVisible(string addonName)
    {
        var addonPtr = _gameGui.GetAddonByName(addonName, 1);
        if (addonPtr == IntPtr.Zero)
        {
            return false;
        }

        var addon = (AtkUnitBase*)addonPtr;
        return addon->IsVisible;
    }

    public bool TryConfirmMateriaExtract()
    {
        if (!IsCallbackReady(GameUiConstants.MateriaExtractConfirmCallbackId, GameUiConstants.MateriaExtractAddonName))
        {
            return false;
        }

        return TryFireCallback(GameUiConstants.MateriaExtractAddonName, GameUiConstants.MateriaExtractConfirmCallbackId);
    }

    public bool TryConfirmDesynth()
    {
        if (!IsCallbackReady(GameUiConstants.DesynthConfirmCallbackId, GameUiConstants.DesynthAddonName))
        {
            return false;
        }

        return TryFireCallback(GameUiConstants.DesynthAddonName, GameUiConstants.DesynthConfirmCallbackId);
    }

    private bool IsCallbackReady(int callbackId, string addonName)
    {
        if (callbackId >= 0)
        {
            return true;
        }

        _pluginLog.Warning($"Callback ID未設定のため操作をスキップします: {addonName}");
        return false;
    }

    private unsafe bool TryFireCallback(string addonName, int callbackId)
    {
        var addonPtr = _gameGui.GetAddonByName(addonName, 1);
        if (addonPtr == IntPtr.Zero)
        {
            _pluginLog.Warning($"アドオンが見つかりません: {addonName}");
            return false;
        }

        var addon = (AtkUnitBase*)addonPtr;
        if (!addon->IsVisible)
        {
            _pluginLog.Warning($"アドオンが非表示です: {addonName}");
            return false;
        }

        // コールバックはパッチ依存のため、失敗時はログに残して次の処理へ進む
        var value = CreateIntValue(callbackId);
        addon->FireCallback(1, &value);
        return true;
    }

    private static AtkValue CreateIntValue(int value)
    {
        return new AtkValue
        {
            Type = ValueType.Int,
            Int = value,
        };
    }
}
