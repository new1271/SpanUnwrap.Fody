using System;

using Fody;

using Mono.Cecil;

namespace SpanDissolve.InSolutionWeaver;

public class SpanDissolveInSolution : ModuleWeaver
{
    static SpanDissolveInSolution()
    {
        GC.KeepAlive(typeof(ModuleDefinition));
        GC.KeepAlive(typeof(WeavingException));
    }
}
