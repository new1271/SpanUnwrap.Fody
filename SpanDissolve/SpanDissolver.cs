using System;

namespace SpanDissolve;

/// <summary>
/// A helper class to dissolve spans into references.
/// </summary>
public static class SpanDissolver
{
    /// <summary>
    /// Dissolve the given span into a reference to its first element.
    /// </summary>
    /// <typeparam name="T">The type of elements in the span.</typeparam>
    /// <param name="span">The span to dissolve.</param>
    /// <returns>A reference to the first element of the span.</returns>
    public static ref T Dissolve<T>(Span<T> span) => throw new NotImplementedException("The weaver is not running.");

    /// <summary>
    /// Dissolve the given span into a reference to its first element.
    /// </summary>
    /// <typeparam name="T">The type of elements in the span.</typeparam>
    /// <param name="span">The span to dissolve.</param>
    /// <returns>A reference to the first element of the span.</returns>
    public static ref readonly T Dissolve<T>(ReadOnlySpan<T> span) => throw new NotImplementedException("The weaver is not running.");
}
