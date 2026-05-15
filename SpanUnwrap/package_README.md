# SpanUnwrap.Fody

This is an add-in for [Fody](https://github.com/Fody/Fody) which lets you unwraps `Span<T>` and `ReadOnlySpan<T>` in compile time.

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
Use the `SpanUnwrap.Unwrap()` method to unwrap a `Span<T>` or `ReadOnlySpan<T>` at compile time and expose its underlying storage directly.<br/><br/>

See the [GitHub repository](https://github.com/new1271/SpanUnwrap.Fody#usage) for more information.