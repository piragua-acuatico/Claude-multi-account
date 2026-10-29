using System.Text.Json.Nodes;
using ClaudeMultiAccount.Core.Messaging;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Tests;

/// <summary>Buzón de mensajes entre perfiles: en disco real, en una carpeta temporal.</summary>
public sealed class MailboxTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 28, 15, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cma buzon {Guid.NewGuid():N}");
    private readonly Mailbox _mailbox;
    private readonly Profile _max = new() { Id = Guid.NewGuid(), Name = "Claude Max", Type = ProfileType.Main };
    private readonly Profile _pro = new() { Id = Guid.NewGuid(), Name = "Claude Pro", Type = ProfileType.DesktopIsolated };

    public MailboxTests() => _mailbox = new Mailbox(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Send_ThenTakeNew_DeliversOnceAndKeepsItInHistory()
    {
        _mailbox.Send(_max, _pro, "  El error está en login.js, línea 40. 👋  ", Now);

        var delivered = _mailbox.TakeNew(_pro.Id);

        var message = Assert.Single(delivered);
        Assert.Equal("El error está en login.js, línea 40. 👋", message.Text);
        Assert.Equal("Claude Max", message.FromName);
        Assert.Empty(_mailbox.TakeNew(_pro.Id));
        Assert.Contains(_mailbox.History(_pro.Id, 10), e => !e.Sent && !e.Unread && e.Message.Id == message.Id);
        Assert.Contains(_mailbox.History(_max.Id, 10), e => e.Sent && e.Message.Id == message.Id);
    }

    [Fact]
    public void Messages_AreDeliveredOldestFirst_AndAtMostFivePerTurn()
    {
        for (var i = 0; i < 7; i++)
        {
            _mailbox.Send(_max, _pro, $"mensaje {i}", Now.AddMinutes(i));
        }

        var first = _mailbox.TakeNew(_pro.Id);

        Assert.Equal(Enumerable.Range(0, 5).Select(i => $"mensaje {i}"), first.Select(m => m.Text));
        Assert.Equal(2, _mailbox.PeekNew(_pro.Id).Count);
        Assert.Equal(["mensaje 5", "mensaje 6"], _mailbox.TakeNew(_pro.Id).Select(m => m.Text));
    }

    [Fact]
    public void Send_RejectsSecrets_AndWritesNothing()
    {
        var error = Assert.Throws<MailboxException>(() =>
            _mailbox.Send(_max, _pro, "Usa esta: sk-ant-api03-ABCDEFGHIJKLMNOPQRSTUV", Now));

        Assert.Contains("clave de Anthropic", error.Message);
        Assert.DoesNotContain("sk-ant", error.Message);
        Assert.Empty(_mailbox.PeekNew(_pro.Id));
        Assert.Empty(_mailbox.History(_max.Id, 10));
    }

    [Fact]
    public void Send_RejectsEmptyTooLongAndToItself()
    {
        Assert.Throws<MailboxException>(() => _mailbox.Send(_max, _pro, "   ", Now));
        Assert.Throws<MailboxException>(() => _mailbox.Send(_max, _pro, new string('a', Mailbox.MaxMessageLength + 1), Now));
        Assert.Throws<MailboxException>(() => _mailbox.Send(_max, _max, "hola", Now));
    }

    [Fact]
    public void AllHistory_CountsEachMessageOnce_AndAllUnreadSeesPendingOnes()
    {
        _mailbox.Send(_max, _pro, "uno", Now);
        _mailbox.Send(_pro, _max, "dos", Now.AddMinutes(1));
        _mailbox.TakeNew(_pro.Id);

        var all = _mailbox.AllHistory(10);

        Assert.Equal(["dos", "uno"], all.Select(e => e.Message.Text));
        Assert.Equal(["dos"], _mailbox.AllUnread().Select(m => m.Text));
    }

    [Fact]
    public void FormatForChat_SaysWhoSentItAndThatTheUserDecides()
    {
        _mailbox.Send(_max, _pro, "Revisa el PR 12.", Now);

        var text = Mailbox.FormatForChat(_mailbox.TakeNew(_pro.Id), stillPending: 2);

        Assert.Contains("De «Claude Max»", text);
        Assert.Contains("Revisa el PR 12.", text);
        Assert.Contains("Quedan 2", text);
        Assert.Contains("no actúes por tu cuenta", text);
        Assert.Equal("", Mailbox.FormatForChat([], 0));
    }

    [Fact]
    public void MarkDelivered_MovesItOnce_SoTheHookDoesNotRepeatIt()
    {
        var message = _mailbox.Send(_max, _pro, "hola", Now, openChat: true, folder: @"C:\proyecto");

        Assert.True(message.OpenChat);
        Assert.Equal(@"C:\proyecto", Assert.Single(_mailbox.PeekNew(_pro.Id)).Folder);
        Assert.True(_mailbox.MarkDelivered(_pro.Id, message.Id));
        Assert.False(_mailbox.MarkDelivered(_pro.Id, message.Id));
        Assert.Empty(_mailbox.TakeNew(_pro.Id));
    }

    [Fact]
    public void ChatLink_IsTheOfficialNewCodeChatLink_WithTextAndFolder()
    {
        var message = _mailbox.Send(_max, _pro, "Revisa login.js & el PR #12, por favor.", Now, openChat: true);

        var (link, complete) = CodeChatLink.Build(message, @"C:\Users\Ana Prueba\web");

        Assert.True(complete);
        Assert.StartsWith("claude://code/new?q=", link);
        var query = new Uri(link).Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Assert.Contains("«Claude Max»", query["q"]);
        Assert.Contains("Revisa login.js & el PR #12, por favor.", query["q"]);
        Assert.Contains("--sin-chat", query["q"]);
        Assert.Equal(@"C:\Users\Ana Prueba\web", query["folder"]);
        Assert.DoesNotContain(" ", link);
    }

    [Fact]
    public void ChatLink_WritesTheFolderTheWayClaudeDesktopUnderstandsIt()
    {
        var message = _mailbox.Send(_max, _pro, "hola", Now, openChat: true);

        var (link, _) = CodeChatLink.Build(message, "C:/Users/Ana Prueba/web/");

        Assert.EndsWith("&folder=" + Uri.EscapeDataString(@"C:\Users\Ana Prueba\web"), link);
        Assert.DoesNotContain("folder=", CodeChatLink.Build(message, "carpeta/relativa").Link);
    }

    [Fact]
    public void ChatLink_ShortensLongMessages_AndSaysTheRestArrivesLater()
    {
        var message = _mailbox.Send(_max, _pro, new string('á', 10_000), Now, openChat: true);

        var (link, complete) = CodeChatLink.Build(message, null);

        Assert.False(complete);
        Assert.True(link.Length < 32_000, $"El enlace mide {link.Length}: no cabe en la línea de comandos.");
        Assert.Contains("el resto te llega", CodeChatLink.Prompt(message).Prompt);
        Assert.DoesNotContain("folder=", link);
    }

    [Fact]
    public void AMessageThatCannotBeRead_IsSkipped_NotAFailure()
    {
        var folder = Path.Combine(_root, _pro.Id.ToString("D"), "nuevos");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "roto.json"), "{ esto no es JSON");

        Assert.Empty(_mailbox.TakeNew(_pro.Id));
    }
}

/// <summary>Hook y skill en la configuración de Claude Code de cada perfil, en disco real.</summary>
public sealed class MessagingSetupTests : IDisposable
{
    private const string Cma = @"C:\Program Files\CMA\cma.exe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cma ajustes {Guid.NewGuid():N}");
    private readonly FakeEnvironment _env = new();
    private readonly MessagingSetup _setup;
    private readonly Profile _main = new() { Id = Guid.NewGuid(), Name = "Claude Max", Type = ProfileType.Main };
    private readonly Profile _pro;

    public MessagingSetupTests()
    {
        _env.Variables["USERPROFILE"] = _root;
        _setup = new MessagingSetup(_env);
        _pro = new Profile
        {
            Id = Guid.NewGuid(),
            Name = "Claude Pro",
            Type = ProfileType.DesktopIsolated,
            ClaudeConfigDir = @"%USERPROFILE%\.multi-claude-pro",
        };
    }

    private string MainSettings => Path.Combine(_root, ".claude", "settings.json");

    private string ProSettings => Path.Combine(_root, ".multi-claude-pro", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Install_AddsHookAndSkill_KeepingTheUsersSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MainSettings)!);
        File.WriteAllText(MainSettings, """
            {
              "model": "opus",
              "hooks": { "UserPromptSubmit": [ { "hooks": [ { "type": "command", "command": "mi-hook.cmd" } ] } ] }
            }
            """);

        Assert.Empty(_setup.Install([_main, _pro], Cma));

        var settings = JsonNode.Parse(File.ReadAllText(MainSettings))!;
        Assert.Equal("opus", settings["model"]!.GetValue<string>());
        var commands = settings["hooks"]!["UserPromptSubmit"]!.AsArray()
            .SelectMany(g => g!["hooks"]!.AsArray()).Select(h => h!["command"]!.GetValue<string>()).ToList();
        Assert.Equal(["mi-hook.cmd", MessagingSetup.HookCommand(Cma, _main.Id)], commands);
        Assert.Equal($"\"C:/Program Files/CMA/cma.exe\" mensaje hook --de {_main.Id:D}", MessagingSetup.HookCommand(Cma, _main.Id));

        Assert.True(_setup.IsInstalled(_pro));
        var skill = File.ReadAllText(Path.Combine(_root, ".multi-claude-pro", "skills", MessagingSetup.SkillName, "SKILL.md"));
        Assert.Contains($"--de {_pro.Id:D}", skill);
        Assert.Contains("«Claude Pro»", skill);
        Assert.StartsWith("---", skill);
    }

    [Fact]
    public void Install_TwiceOrWithANewPath_LeavesASingleHook()
    {
        _setup.Install([_pro], Cma);
        var written = File.GetLastWriteTimeUtc(ProSettings);
        _setup.Install([_pro], Cma);
        Assert.Equal(written, File.GetLastWriteTimeUtc(ProSettings));

        _setup.Install([_pro], @"D:\otra\cma.exe");

        var groups = JsonNode.Parse(File.ReadAllText(ProSettings))!["hooks"]!["UserPromptSubmit"]!.AsArray();
        var group = Assert.Single(groups);
        Assert.Contains("D:/otra/cma.exe", group!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Remove_TakesOutOnlyWhatTheToolAdded()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MainSettings)!);
        File.WriteAllText(MainSettings, """{ "model": "opus", "hooks": { "Stop": [] } }""");
        _setup.Install([_main], Cma);

        Assert.Empty(_setup.Remove([_main]));

        var settings = JsonNode.Parse(File.ReadAllText(MainSettings))!.AsObject();
        Assert.Equal("opus", settings["model"]!.GetValue<string>());
        Assert.NotNull(settings["hooks"]!["Stop"]);
        Assert.Null(settings["hooks"]!["UserPromptSubmit"]);
        Assert.False(_setup.IsInstalled(_main));
        Assert.False(Directory.Exists(Path.Combine(_root, ".claude", "skills", MessagingSetup.SkillName)));
    }

    [Fact]
    public void InvalidSettings_AreNeverOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MainSettings)!);
        File.WriteAllText(MainSettings, "{ esto no es JSON");

        var problem = Assert.Single(_setup.Install([_main], Cma));

        Assert.Contains("Claude Max", problem);
        Assert.Equal("{ esto no es JSON", File.ReadAllText(MainSettings));
    }

    [Fact]
    public void TerminalProfiles_AreLeftAlone()
    {
        var cli = new Profile { Id = Guid.NewGuid(), Name = "Terminal", Type = ProfileType.Cli, ClaudeConfigDir = @"%USERPROFILE%\.cli" };

        Assert.Empty(_setup.Install([cli], Cma));

        Assert.False(Directory.Exists(Path.Combine(_root, ".cli")));
    }

    [Fact]
    public void Mailbox_LivesOutsideAppData_SoMsixDoesNotHideIt()
    {
        var paths = AppPaths.FromEnvironment(new FakeEnvironment());

        Assert.Equal(@"C:\Users\prueba\.claude-multi-account\buzon", paths.MailboxDir);
        Assert.DoesNotContain("AppData", paths.MailboxDir, StringComparison.OrdinalIgnoreCase);
    }
}
