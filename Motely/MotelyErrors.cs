namespace Motely;

/// <summary>
/// Where the engine says why it is about to throw. A host that loses exception messages at its
/// boundary (the browser-wasm build, where NativeAOT hands JS a bare "C# exception from NativeAOT")
/// subscribes to <see cref="Thrown"/> and keeps the reason. Throw sites call
/// <c>throw MotelyErrors.Report(new ...)</c>; with no subscriber it is just the throw.
/// </summary>
public static class MotelyErrors
{
    /// <summary>Raised with each exception the engine reports, right before it is thrown.</summary>
    public static event Action<Exception>? Thrown;

    /// <summary>Tells <see cref="Thrown"/> subscribers, then hands the exception back to throw.</summary>
    /// <param name="exception">The exception about to be thrown.</param>
    /// <returns>The same exception.</returns>
    public static Exception Report(Exception exception)
    {
        Thrown?.Invoke(exception);
        return exception;
    }
}
