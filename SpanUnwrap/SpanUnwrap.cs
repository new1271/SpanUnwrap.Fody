using System;

namespace SpanUnwrap;

/// <summary>
/// A helper class to unwrap spans into references.
/// </summary>
public static class SpanUnwrap
{
    /// <summary>
    /// Unwrap the given span into a reference to its first element.
    /// </summary>
    /// <typeparam name="T">The type of elements in the span.</typeparam>
    /// <param name="span">The span to dissolve.</param>
    /// <returns>A reference to the first element of the span.</returns>
    public static ref T Unwrap<T>(Span<T> span) => throw new NotImplementedException("The weaver is not running.");

    /// <summary>
    /// Unwrap the given span into a reference to its first element.
    /// </summary>
    /// <typeparam name="T">The type of elements in the span.</typeparam>
    /// <param name="span">The span to dissolve.</param>
    /// <returns>A reference to the first element of the span.</returns>
    public static ref readonly T Unwrap<T>(ReadOnlySpan<T> span) => throw new NotImplementedException("The weaver is not running.");
}
