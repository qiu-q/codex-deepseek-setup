using System.Windows;
using CodexDeepSeekSetup.Core.Proxy;

namespace CodexDeepSeekSetup.App;

public partial class NodeSelectionDialog : Window
{
    private readonly INetworkNodeActions actions;
    private readonly CancellationTokenSource lifetime = new();

    public NodeSelectionDialog(INetworkNodeActions actions, IReadOnlyList<ProxyNode> nodes)
    {
        InitializeComponent();
        this.actions = actions;
        var items = nodes.Select(node => new NodeChoice(
            node.Name,
            GetDisplayName(node.Name),
            node.Name == "AUTO" ? "由 Mihomo 自动选择当前延迟较低的节点" : "订阅节点",
            node.IsSelected ? Visibility.Visible : Visibility.Collapsed)).ToArray();
        NodeList.ItemsSource = items;
        NodeList.SelectedItem = items.FirstOrDefault(item => item.CurrentVisibility == Visibility.Visible)
            ?? items.FirstOrDefault();
    }

    public string SelectedNodeDisplayName { get; private set; } = "自动选择";

    public static string GetDisplayName(string name) =>
        name == "AUTO" ? "自动选择（推荐）" : name;

    protected override void OnClosed(EventArgs e)
    {
        lifetime.Cancel();
        lifetime.Dispose();
        base.OnClosed(e);
    }

    private async void TestDelayButton_Click(object sender, RoutedEventArgs e)
    {
        if (NodeList.SelectedItem is not NodeChoice selected)
        {
            NodeStatusText.Text = "请先选择一个节点。";
            return;
        }

        SetButtons(false);
        NodeStatusText.Text = $"正在测试 {selected.DisplayName}……";
        var result = await actions.GetNetworkNodeDelayAsync(selected.Name, lifetime.Token);
        SetButtons(true);
        NodeStatusText.Text = result.IsSuccess
            ? $"{selected.DisplayName}：{result.Value} ms"
            : result.ErrorMessage ?? "延迟测试失败，请选择其他节点重试。";
    }

    private async void ApplyNodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (NodeList.SelectedItem is not NodeChoice selected)
        {
            NodeStatusText.Text = "请先选择一个节点。";
            return;
        }

        await ApplyAsync(selected);
    }

    private async void UseAutoButton_Click(object sender, RoutedEventArgs e)
    {
        var automatic = (NodeList.ItemsSource as IEnumerable<NodeChoice>)?.FirstOrDefault(item => item.Name == "AUTO");
        if (automatic is null)
        {
            NodeStatusText.Text = "当前配置没有自动选择组。";
            return;
        }

        NodeList.SelectedItem = automatic;
        await ApplyAsync(automatic);
    }

    private async Task ApplyAsync(NodeChoice selected)
    {
        SetButtons(false);
        NodeStatusText.Text = $"正在应用 {selected.DisplayName}……";
        var result = await actions.SelectNetworkNodeAsync(selected.Name, lifetime.Token);
        SetButtons(true);
        if (!result.IsSuccess)
        {
            NodeStatusText.Text = result.ErrorMessage ?? "节点切换失败，请重试。";
            return;
        }

        SelectedNodeDisplayName = selected.DisplayName;
        DialogResult = true;
    }

    private void SetButtons(bool enabled)
    {
        TestDelayButton.IsEnabled = enabled;
        UseAutoButton.IsEnabled = enabled;
        ApplyNodeButton.IsEnabled = enabled;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record NodeChoice(
        string Name,
        string DisplayName,
        string Description,
        Visibility CurrentVisibility);
}
