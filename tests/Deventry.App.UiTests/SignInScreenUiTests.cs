using Avalonia.Headless.XUnit;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.Views;

namespace Deventry.App.UiTests;

/// <summary>
/// The sign-in screen rendered with its <b>Use Ollama or llama.cpp instead</b> (DESIGN.md §18, "Without a Claude
/// account"): the offer under Sign in, then the Ollama form it opens.
/// </summary>
public class SignInScreenUiTests
{
    [AvaloniaFact]
    public async Task Ollama_or_llama_cpp_is_offered_instead_of_a_sign_in_with_the_settings_form()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var signIn = new SignInViewModel(h.Services, () => Task.CompletedTask) { LocalAccount = new LocalAccountOfferViewModel(h.Services, () => Task.CompletedTask) };
        var window = UiText.Show(new SignInView { DataContext = signIn }, 900, 900);
        UiText.Settle(window);
        var closed = UiText.Describe(window);

        signIn.LocalAccount.OpenCommand.Execute(null);
        UiText.Settle(window);

        await Verify($"{closed}\n---- Use Ollama or llama.cpp instead ----\n{UiText.Describe(window)}");
    }
}
