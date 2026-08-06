from pathlib import Path
import re

path = Path("src/Cinder.MINT.App/ViewModels/MainViewModel.cs")
content = path.read_text(encoding="utf-8")
pattern = re.compile(
    r"    private void OnEngineFaulted\(object\? sender, string message\)\n    \{.*?\n    \}\n\n    private void RunWatchdog\(\)",
    re.DOTALL,
)
replacement = '''    private void OnEngineFaulted(object? sender, string message)
    {
        bool feedbackGuard = message.StartsWith(
            "FEEDBACK GUARD",
            StringComparison.OrdinalIgnoreCase);

        App.Current.Dispatcher.BeginInvoke(() =>
        {
            if (feedbackGuard)
            {
                _engine.Stop();
                IsRunning = false;
                _restartPending = false;
                StatusText = $"SAFETY STOP — {message}";
                return;
            }

            IsRunning = false;
            _restartPending = AutoStart;
            StatusText = $"RECOVERING — {message}";
        });
    }

    private void RunWatchdog()'''
updated, count = pattern.subn(replacement, content, count=1)
if count != 1:
    raise SystemExit("Feedback fault handler patch did not match exactly once.")
path.write_text(updated, encoding="utf-8", newline="\n")
