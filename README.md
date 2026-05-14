# SpanDissolve.Fody
[![NuGet package](https://img.shields.io/nuget/v/SpanDissolve.Fody.svg?logo=NuGet)](https://www.nuget.org/packages/SpanDissolve.Fody)

This is an add-in for [Fody](https://github.com/Fody/Fody) which lets you dissolves `Span<T>` and `ReadOnlySpan<T>` in compile time.

---

 - [Installation](#installation)
 - [Usage](#usage)
 - [Example](#example)

---

## Installation

- Install the NuGet packages [`Fody`](https://www.nuget.org/packages/Fody) and [`SpanDissolve.Fody`](https://www.nuget.org/packages/SpanDissolve.Fody). Installing `Fody` explicitly is needed to enable weaving.

  ```
  PM> Install-Package Fody
  PM> Install-Package SpanDissolve.Fody
  ```

- Add the `PrivateAssets="all"` metadata attribute to the `<PackageReference />` items of `Fody` and `SpanDissolve.Fody` in your project file, so they won't be listed as dependencies.

- If you already have a `FodyWeavers.xml` file in the root directory of your project, add the `<SpanDissolve />` tag there. This file will be created on the first build if it doesn't exist:

  ```XML
  <?xml version="1.0" encoding="utf-8" ?>
  <Weavers>
    <SpanDissolve />
  </Weavers>
  ```

See [Fody usage](https://github.com/Fody/Home/blob/master/pages/usage.md) for general guidelines, and [Fody Configuration](https://github.com/Fody/Home/blob/master/pages/configuration.md) for additional options.

## Usage
Use the `SpanDissolver.Dissolve()` method to deconstruct a `Span<T>` or `ReadOnlySpan<T>` at compile time and expose its underlying storage directly.<br/><br/>
`SpanDissolver.Fody` intercepts the call during compilation and optimizes the access based on the data source:
- For the constant `ReadOnlySpan<T>` declarations: It will expose the "raw" data field, which is embedded by the compiler in your assembly, as a `ref readonly` reference.
- For the `Span<T>` from `stackalloc`: It will expose the space you allocated from the stack as a `ref` reference.
- For the other cases: It will automatically route the call to the `GetPinnableReference()` method in the `Span<T>` or `ReadOnlySpan<T>` instance.

## Example

What you write:

```csharp
public static void CopyData(ref byte destination)
{
    
    const int Length = 8;
    CopyBlockUnaligned(
        ref destination,
        in SpanDissolver.Dissolve(
            // a constant, length-sealed ReadOnlySpan<byte>.
            new byte[Length] { 0xFF, 0x5E, 0x30, 0x21, 0x44, 0x55, 0x55, 0x1A }
        ),
        Length);
}
```

What gets compiled:<br/><br/>
C# disassmbly:

```csharp
using System.Runtime.CompilerServices;

public static void CopyData(ref byte destination)
{
    CopyBlockUnaligned(ref destination, in Unsafe.As<long, byte>(
      // The "raw" data field is exposed now, and can be accessed directly by your code
      ref global::<PrivateImplementationDetails>.70DE15E71C9CEE3760BB7174ECC485889F6F554DDDC4947D4ECAF87E48003025)
      , 8u);
}
```
raw MSIL code:
```msil
.method public hidebysig static 
    void CopyData (
        uint8& destination
    ) cil managed 
{
    // Method begins at RVA 0x2626
    // Header size: 1
    // Code size: 14 (0xe)
    .maxstack 8

    IL_0000: ldarg.0
    IL_0001: ldsflda int64 '<PrivateImplementationDetails>'::'70DE15E71C9CEE3760BB7174ECC485889F6F554DDDC4947D4ECAF87E48003025'
    IL_0006: ldc.i4.8
    IL_0007: conv.i
    IL_0008: call void SpanDissolve.Example.Stores::CopyBlockUnaligned(uint8&, uint8&, native uint)
    IL_000d: ret
} // end of method Stores::CopyData

```
