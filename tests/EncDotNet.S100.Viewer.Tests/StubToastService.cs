using System;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>No-op <see cref="IToastService"/> for unit tests.</summary>
internal sealed class StubToastService : IToastService
{
    public void ShowInfo(string title, string? content = null) { }
    public void ShowSuccess(string title, string? content = null) { }
    public void ShowWarning(string title, string? content = null) { }
    public void ShowError(string title, string? content = null, string? actionLabel = null, Action? action = null, bool sticky = false) { }
    public void ShowLoading(string title, string? content = null, string? actionLabel = null, Action? action = null) { }
    public void DismissAll() { }
}
