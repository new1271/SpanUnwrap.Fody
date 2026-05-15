# SpanUnwrap.Fody
[![NuGet package](https://img.shields.io/nuget/v/SpanUnwrap.Fody.svg?logo=NuGet)](https://www.nuget.org/packages/SpanUnwrap.Fody)

This is an add-in for [Fody](https://github.com/Fody/Fody) which lets you unwraps `Span<T>` and `ReadOnlySpan<T>` in compile time.

---

 - [Installation](#installation)
 - [Usage](#usage)
 - [Example](#example)

---

## Installation
*(Note: The following configurations are intended for SDK-style projects)*<br/><br/>
### **1. Core Packages and Settings**<br/>
- Install the NuGet packages [`Fody`](https://www.nuget.org/packages/Fody) and [`SpanUnwrap.Fody`](https://www.nuget.org/packages/SpanUnwrap.Fody). Installing `Fody` explicitly is needed to enable weaving.
  
  ```xml
  <PackageReference Include="Fody" Version="*">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  <PackageReference Include="SpanUnwrap.Fody" Version="*">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  ```
- If you already have a `FodyWeavers.xml` file in the root directory of your project, add the `<SpanUnwrap />` tag there. This file will be created on the first build if it doesn't exist:

  ```XML
  <?xml version="1.0" encoding="utf-8" ?>
  <Weavers>
      <SpanUnwrap />
  </Weavers>
  ```
See [Fody usage](https://github.com/Fody/Home/blob/master/pages/usage.md) for general guidelines, and [Fody Configuration](https://github.com/Fody/Home/blob/master/pages/configuration.md) for additional options.

### **2. Legacy Framework Support**<br/>
If your project targets **.NET Standard 2.0 (or lower)**, **.NET Core 2.x (or lower)**, or **.NET Framework**, you must include `System.Memory` manually.

  ```xml
  <PackageReference Include="System.Memory" Version="*" />
  ```
**Tip: Zero-Runtime Dependency**: If you only need System.Memory for compile-time constant access and want to keep your runtime clean, use the following configuration instead:
  ```xml
  <PackageReference Include="System.Memory" Version="*">
      <PrivateAssets>all</PrivateAssets>
      <ExcludeAssets>runtime</ExcludeAssets>
      <IncludeAssets>compile; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  ```

## Usage
Use the `Unwrap.From()` method to unwrap a `Span<T>` or `ReadOnlySpan<T>` at compile time and expose its underlying storage directly.<br/><br/>
`SpanUnwrap.Fody` intercepts the call during compilation and optimizes the access based on the data source:
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
        in Unwrap.From(
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
    IL_0008: call void SpanUnwrap.Example.Stores::CopyBlockUnaligned(uint8&, uint8&, native uint)
    IL_000d: ret
} // end of method Stores::CopyData

```
