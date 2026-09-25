using System.Runtime.CompilerServices;
using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using VerifyTests;

namespace Motely.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyDiffPlex.Initialize();

        VerifierSettings.TreatAsString<StringBuilder>();
    }
}
