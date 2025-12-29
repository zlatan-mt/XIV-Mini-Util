// Path: projects/XIV-Mini-Util/Services/GameUiService.cs
// Description: DalamudのGameGuiを通じてアドオン操作を行う
// Reason: UI操作の責務をサービスに分離して保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/GameUiConstants.cs, projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Services/DesynthService.cs
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

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
        nint addonPtr = _gameGui.GetAddonByName(addonName, 1);
        if (addonPtr == nint.Zero)
        {
            return false;
        }

        var addon = (AtkUnitBase*)addonPtr;
        return addon->IsVisible;
    }

    public bool TrySelectMaterializeFirstItem()
    {
        return TryFireCallbackWithFallback(
            GameUiConstants.MaterializeAddonName,
            GameUiConstants.MaterializeSelectCallbackPrimaryCount,
            GameUiConstants.MaterializeSelectCallbackFallbackCount,
            true,
            GameUiConstants.MaterializeSelectCallbackValue0,
            GameUiConstants.MaterializeSelectCallbackValue1);
    }

    public unsafe bool TryConfirmMaterializeDialog()
    {
        nint addonPtr = _gameGui.GetAddonByName(GameUiConstants.MaterializeDialogAddonName, 1);
        if (addonPtr == nint.Zero)
        {
            _pluginLog.Warning($"アドオンが見つかりません: {GameUiConstants.MaterializeDialogAddonName}");
            return false;
        }

        var addon = (AddonMaterializeDialog*)addonPtr;
        if (addon->YesButton == null)
        {
            _pluginLog.Warning("マテリア精製のYesボタンが取得できません。");
            return false;
        }

        ClickAddonButton(addon->YesButton, (AtkUnitBase*)addon);
        return true;
    }

    public bool TryConfirmDesynth()
    {
        return TryFireCallback(
            GameUiConstants.SalvageDialogAddonName,
            true,
            GameUiConstants.SalvageDialogConfirmValue0,
            GameUiConstants.SalvageDialogConfirmValue1);
    }

    public unsafe bool IsSalvageResultOpen()
    {
        if (!TryGetAgent(AgentId.Salvage, out var agent))
        {
            return false;
        }

        var salvageAgent = (AgentSalvage*)agent;
        return salvageAgent->IsSalvageResultAddonOpen;
    }

    public bool TryCloseSalvageResult()
    {
        var closed = false;
        if (IsAddonVisible(GameUiConstants.SalvageResultAddonName))
        {
            closed |= TryCloseAddon(GameUiConstants.SalvageResultAddonName);
        }

        if (IsAddonVisible(GameUiConstants.SalvageAutoDialogAddonName))
        {
            closed |= TryCloseAddon(GameUiConstants.SalvageAutoDialogAddonName);
        }

        return closed;
    }

    public unsafe bool TryRefreshSalvageItemList()
    {
        if (!TryGetAgent(AgentId.Salvage, out var agent))
        {
            return false;
        }

        var salvageAgent = (AgentSalvage*)agent;
        salvageAgent->ItemListRefresh(salvageAgent->IsSalvageResultAddonOpen);
        return true;
    }

    public unsafe bool TryOpenSalvageItemSelector()
    {
        if (!TryGetAgent(AgentId.Salvage, out var agent))
        {
            return false;
        }

        if (agent->IsAddonShown())
        {
            return true;
        }

        if (!agent->IsActivatable())
        {
            _pluginLog.Warning("分解ウィンドウが開けない状態です。");
            return false;
        }

        // Agentを明示的に起動してからアドオン表示を試みる
        agent->Show();
        agent->ShowAddon();
        return agent->IsAddonShown();
    }

    public bool TrySelectSalvageItem(InventoryItemInfo item)
    {
        if (TrySelectSalvageItemWithAgent(item))
        {
            return true;
        }

        return TryFireCallback(
            GameUiConstants.SalvageItemSelectorAddonName,
            true,
            GameUiConstants.SalvageItemSelectValue0,
            (uint)item.Container,
            (uint)item.Slot);
    }

    private unsafe bool TrySelectSalvageItemWithAgent(InventoryItemInfo item)
    {
        if (!TryGetAgent(AgentId.Salvage, out var agent))
        {
            return false;
        }

        if (!agent->IsAddonShown())
        {
            if (!TryOpenSalvageItemSelector())
            {
                return false;
            }
        }

        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            _pluginLog.Warning("InventoryManagerが取得できません。");
            return false;
        }

        var container = manager->GetInventoryContainer(item.Container);
        if (container == null)
        {
            _pluginLog.Warning($"インベントリコンテナが見つかりません: {item.Container}");
            return false;
        }

        var slot = container->GetInventorySlot(item.Slot);
        if (slot == null)
        {
            _pluginLog.Warning($"インベントリスロットが見つかりません: {item.Container}:{item.Slot}");
            return false;
        }

        // Agent経由で分解対象を指定し、確認ダイアログ表示を促す
        var salvageAgent = (AgentSalvage*)agent;
        salvageAgent->SalvageItem(slot, (int)agent->AddonId, 0);
        return true;
    }

    private unsafe bool TryFireCallback(string addonName, bool updateState, params object[] values)
    {
        if (!TryGetAddon(addonName, out var addon))
        {
            return false;
        }

        if (values.Length == 0)
        {
            _pluginLog.Warning($"Callback値が空のためスキップします: {addonName}");
            return false;
        }

        var atkValues = stackalloc AtkValue[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (!TryBuildAtkValue(values[i], out atkValues[i]))
            {
                _pluginLog.Warning($"未対応の型のためコールバックを中止します: {addonName}");
                return false;
            }
        }

        return addon->FireCallback((uint)values.Length, atkValues, updateState);
    }

    private unsafe bool TryGetAgent(AgentId agentId, out AgentInterface* agent)
    {
        var agentModule = AgentModule.Instance();
        if (agentModule == null)
        {
            _pluginLog.Warning("AgentModuleが取得できません。");
            agent = null;
            return false;
        }

        agent = agentModule->GetAgentByInternalId(agentId);
        if (agent == null)
        {
            _pluginLog.Warning($"Agentが取得できません: {agentId}");
            return false;
        }

        return true;
    }

    private unsafe bool TryFireCallbackWithFallback(
        string addonName,
        int primaryCount,
        int fallbackCount,
        bool updateState,
        params object[] values)
    {
        if (!TryGetAddon(addonName, out var addon))
        {
            return false;
        }

        if (values.Length == 0)
        {
            _pluginLog.Warning($"Callback値が空のためスキップします: {addonName}");
            return false;
        }

        var atkValues = stackalloc AtkValue[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (!TryBuildAtkValue(values[i], out atkValues[i]))
            {
                _pluginLog.Warning($"未対応の型のためコールバックを中止します: {addonName}");
                return false;
            }
        }

        if (addon->FireCallback((uint)primaryCount, atkValues, updateState))
        {
            return true;
        }

        return addon->FireCallback((uint)fallbackCount, atkValues, updateState);
    }

    private unsafe bool TryGetAddon(string addonName, out AtkUnitBase* addon)
    {
        nint addonPtr = _gameGui.GetAddonByName(addonName, 1);
        if (addonPtr == nint.Zero)
        {
            _pluginLog.Warning($"アドオンが見つかりません: {addonName}");
            addon = null;
            return false;
        }

        addon = (AtkUnitBase*)addonPtr;
        if (!addon->IsVisible)
        {
            _pluginLog.Warning($"アドオンが非表示です: {addonName}");
            return false;
        }

        return true;
    }

    private unsafe bool TryCloseAddon(string addonName)
    {
        if (!TryGetAddon(addonName, out var addon))
        {
            return false;
        }

        return addon->Close(true);
    }

    private static bool TryBuildAtkValue(object value, out AtkValue atkValue)
    {
        atkValue = new AtkValue();
        switch (value)
        {
            case int intValue:
                atkValue.Type = ValueType.Int;
                atkValue.Int = intValue;
                return true;
            case uint uintValue:
                atkValue.Type = ValueType.UInt;
                atkValue.UInt = uintValue;
                return true;
            case bool boolValue:
                atkValue.Type = ValueType.Bool;
                atkValue.Byte = (byte)(boolValue ? 1 : 0);
                return true;
            default:
                return false;
        }
    }

    private static unsafe void ClickAddonButton(AtkComponentButton* button, AtkUnitBase* addon)
    {
        var buttonNode = button->AtkComponentBase.OwnerNode;
        var resNode = buttonNode->AtkResNode;
        var evt = (AtkEvent*)resNode.AtkEventManager.Event;
        addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, resNode.AtkEventManager.Event);
    }
}
