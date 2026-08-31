// Path: projects/XIV-Mini-Util/Windows/Components/ITabComponent.cs
// Description: タブ描画の共通インターフェース
// Reason: MainWindowの責務をタブ単位に分割するため
namespace XivMiniUtil.Windows.Components;

public interface ITabComponent : IDisposable
{
    void Draw();
}
