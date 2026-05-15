using System;

using Fody;

using Mono.Cecil;

namespace SpanUnwrap.InSolutionWeaver;

public class SpanUnwrapInSolution : ModuleWeaver
{
    static SpanUnwrapInSolution()
    {
        GC.KeepAlive(typeof(ModuleDefinition));
        GC.KeepAlive(typeof(WeavingException));
    }
}
