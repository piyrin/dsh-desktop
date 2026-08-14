using System;

internal static class TrayControllerTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("tray menu has the exact command order", delegate {
            FakeTrayViewFactory factory = new FakeTrayViewFactory();
            using (TrayController tray = new TrayController("app.ico", factory))
            {
                AssertEx.Equal(5, factory.Entries.Length);
                AssertEntry(factory.Entries[0], "Open", TrayCommand.Open, false);
                AssertEntry(factory.Entries[1], "Restart Service", TrayCommand.RestartService, false);
                AssertEntry(factory.Entries[2], "View Logs", TrayCommand.ViewLogs, false);
                AssertEntry(factory.Entries[3], null, TrayCommand.Separator, true);
                AssertEntry(factory.Entries[4], "Exit", TrayCommand.Exit, false);
            }
        });

        runner.Add("tray double click maps to Open", delegate {
            FakeTrayViewFactory factory = new FakeTrayViewFactory();
            using (TrayController tray = new TrayController("app.ico", factory))
            {
                int opens = 0;
                tray.OpenRequested += delegate { opens++; };
                factory.View.RaiseDoubleClick();
                AssertEx.Equal(1, opens);
            }
        });

        runner.Add("tray command events map to application actions", delegate {
            FakeTrayViewFactory factory = new FakeTrayViewFactory();
            using (TrayController tray = new TrayController("app.ico", factory))
            {
                int restarts = 0;
                int logs = 0;
                int exits = 0;
                tray.RestartServiceRequested += delegate { restarts++; };
                tray.ViewLogsRequested += delegate { logs++; };
                tray.ExitRequested += delegate { exits++; };
                factory.View.RaiseCommand(TrayCommand.RestartService);
                factory.View.RaiseCommand(TrayCommand.ViewLogs);
                factory.View.RaiseCommand(TrayCommand.Exit);
                AssertEx.Equal(1, restarts);
                AssertEx.Equal(1, logs);
                AssertEx.Equal(1, exits);
            }
        });

        runner.Add("tray disposal hides and disposes its platform icon", delegate {
            FakeTrayViewFactory factory = new FakeTrayViewFactory();
            TrayController tray = new TrayController("app.ico", factory);

            tray.Dispose();
            tray.Dispose();

            AssertEx.Equal(1, factory.View.DisposeCalls);
        });
    }

    private static void AssertEntry(TrayMenuEntry entry, string text, TrayCommand command, bool separator)
    {
        AssertEx.Equal(text, entry.Text);
        AssertEx.Equal(command, entry.Command);
        AssertEx.Equal(separator, entry.IsSeparator);
    }

    private sealed class FakeTrayViewFactory : ITrayIconViewFactory
    {
        internal TrayMenuEntry[] Entries;
        internal readonly FakeTrayView View = new FakeTrayView();
        public ITrayIconView Create(string iconPath, TrayMenuEntry[] entries)
        {
            AssertEx.Equal("app.ico", iconPath);
            Entries = entries;
            return View;
        }
    }

    private sealed class FakeTrayView : ITrayIconView
    {
        public event EventHandler DoubleClicked;
        public event EventHandler<TrayCommandEventArgs> CommandInvoked;
        internal int DisposeCalls;
        public void SetRestartEnabled(bool enabled) { }
        public void Dispose() { DisposeCalls++; }
        internal void RaiseDoubleClick()
        {
            EventHandler handler = DoubleClicked;
            if (handler != null) handler(this, EventArgs.Empty);
        }
        internal void RaiseCommand(TrayCommand command)
        {
            EventHandler<TrayCommandEventArgs> handler = CommandInvoked;
            if (handler != null) handler(this, new TrayCommandEventArgs(command));
        }
    }
}
