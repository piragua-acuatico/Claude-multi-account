using ClaudeMultiAccount.App.ViewModels;
using ClaudeMultiAccount.Core.Messaging;

namespace ClaudeMultiAccount.App.Tests;

public class MessagesViewModelTests
{
    private readonly FakeProfileService _service = new();

    private static MailboxMessage Message(string text, DateTime sentUtc) =>
        new(Guid.NewGuid().ToString("N"), Guid.NewGuid(), "Claude Max", Guid.NewGuid(), "Claude Pro", sentUtc, text);

    [Fact]
    public async Task Toggle_ActivatesAndThenDeactivates()
    {
        var vm = new MessagesViewModel(_service);
        await vm.LoadAsync();
        Assert.False(vm.IsEnabled);
        Assert.True(vm.HasNoMessages);

        await vm.ToggleCommand.ExecuteAsync();
        Assert.True(vm.IsEnabled);
        Assert.Contains("chat de Code nuevo", vm.Message);

        await vm.ToggleCommand.ExecuteAsync();
        Assert.False(vm.IsEnabled);
        Assert.Contains("historial se conserva", vm.Message);
    }

    [Fact]
    public async Task Problems_AreShown()
    {
        _service.MessagingProblems.Add("«Claude Max»: su settings.json no es JSON válido; no se tocó.");
        var vm = new MessagesViewModel(_service);

        await vm.ToggleCommand.ExecuteAsync();

        Assert.Single(vm.Problems);
        Assert.Contains("no se pudo preparar", vm.Message);
    }

    [Fact]
    public async Task History_ShowsWhoWroteToWhom()
    {
        _service.Messages.Add(new MailboxEntry(Message("Revisa el PR 12.", DateTime.UtcNow), Sent: false, Unread: true));
        var vm = new MessagesViewModel(_service);

        await vm.LoadAsync();

        var row = Assert.Single(vm.Messages);
        Assert.Equal("Claude Max  →  Claude Pro", row.Direction);
        Assert.StartsWith("hoy, ", row.When);
        Assert.True(row.Unread);
        Assert.True(vm.HasMessages);
    }

    [Fact]
    public async Task MainWindow_NotifiesEachNewMessageOnce()
    {
        var main = new MainViewModel(_service, new FakeDialogService(answer: true));
        var notices = new List<string>();
        main.MessageArrived += notices.Add;
        _service.Unread.Add(Message("hola", DateTime.UtcNow));

        await main.CheckNewMessagesAsync();
        await main.CheckNewMessagesAsync();

        var notice = Assert.Single(notices);
        Assert.Contains("«Claude Max» dejó un mensaje para «Claude Pro»", notice);
    }

    [Fact]
    public async Task Startup_KeepsTheProfilesUpToDate()
    {
        var main = new MainViewModel(_service, new FakeDialogService(answer: true));

        await main.InitializeAsync(new ClaudeMultiAccount.App.Services.StartupOptions(LaunchProfile: null));

        Assert.Equal(1, _service.MessagingSyncs);
    }

    [Fact]
    public async Task OpenedChat_IsAnnouncedOnce_NotAsAPlainMessageToo()
    {
        var main = new MainViewModel(_service, new FakeDialogService(answer: true));
        var notices = new List<string>();
        main.MessageArrived += notices.Add;
        var message = Message("hola", DateTime.UtcNow);
        _service.ChatsToOpen.Add(new ClaudeMultiAccount.App.Services.OpenedChat(message.Id, "«Claude Max» te abrió un chat en «Claude Pro»."));
        _service.Unread.Add(message);

        await main.CheckNewMessagesAsync();

        Assert.Equal(["«Claude Max» te abrió un chat en «Claude Pro»."], notices);
    }
}
