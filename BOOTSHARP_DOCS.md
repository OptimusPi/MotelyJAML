---
layout: home
markdownStyles: false
title: Use C# in web apps with comfort
titleTemplate: Bootsharp • :title

hero:
  name: Bootsharp
  text: Use C# in web apps with comfort
  tagline: Author the domain in C#, while fully leveraging the modern TypeScript frontend ecosystem.
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: View on GitHub
      link: https://github.com/elringus/bootsharp
  image:
    src: /favicon.svg
    alt: Bootsharp
---

<div class="features">
    <div class="container">
        <div class="items" style="margin: -8px">
            <div class="items">
                <div class="grid-3 item">
                    <div class="VPLink no-icon VPFeature">
                        <article class="box">
                            <div class="box-title">
                                <div class="icon">✨</div>
                                <h2 class="title">High-level Interoperation</h2>
                            </div>
                            <p class="details">Generates JavaScript bindings and TypeScript declarations for your C# APIs, enabling seamless interop between domain and UI.</p></article>
                    </div>
                </div>
                <div class="grid-3 item">
                    <div class="VPLink no-icon VPFeature">
                        <article class="box">
                            <div class="box-title">
                                <div class="icon">📦</div>
                                <h2 class="title">Modern ES Package</h2>
                            </div>
                            <p class="details">Just run "dotnet publish" and get a full-fledged ES package with "package.json" included—directly importable into your web project.</p></article>
                    </div>
                </div>
                <div class="grid-3 item">
                    <div class="VPLink no-icon VPFeature">
                        <article class="box">
                            <div class="box-title">
                                <div class="icon">🗺️</div>
                                <h2 class="title">Runs Everywhere</h2>
                            </div>
                            <p class="details">Node, Deno, Bun, web browsers—even constrained environments like VS Code extensions—your app runs everywhere.</p></article>
                    </div>
                </div>
            </div>
            <div class="items">
                <div class="grid-4 item">
                    <div class="VPLink no-icon VPFeature">
                        <article class="box">
                            <div class="box-title">
                                <div class="icon">🧩</div>
                                <h2 class="title">Interop Modules</h2>
                            </div>
                            <p class="details">Author fine-grained bindings for C# members, or feed Bootsharp entire API surfaces—it'll handle the rest.</p></article>
                    </div>
                </div>
                <div class="grid-4 item">
                    <div class="VPLink no-icon VPFeature">
                        <article class="box">
                            <div class="box-title">
                                <div class="icon">🧬</div>
                                <h2 class="title">Type Polyglot</h2>
                            </div>
                            <p class="details">Intelligently supports any type: immutables are copied with a fast binary serializer, others passed by reference—fully automated.</p></article>
                    </div>
                </div>
                <div class="grid-4 item">
                    <div class="VPLink no-icon VPFeature">
                        <article class="box">
                            <div class="box-title">
                                <div class="icon">🛠️</div>
                                <h2 class="title">Customizable</h2>
                            </div>
                            <p class="details">Configure namespaces for emitted bindings, function and event names, C# -> TypeScript type mappings, and more.</p></article>
                    </div>
                </div>
                <div class="grid-4 item">
                    <div class="VPLink no-icon VPFeature">
                        <article class="box">
                            <div class="box-title">
                                <div class="icon">⚡</div>
                                <h2 class="title">Fast and Tiny</h2>
                            </div>
                            <p class="details">Compiles WASM with NativeAOT-LLVM and further optimizes with Binaryen for optimal performance and minimal bundle size.</p></article>
                    </div>
                </div>
            </div>
        </div>
    </div>
</div>

<style>
body {
    --vp-home-hero-name-color: transparent;
    --vp-home-hero-name-background: -webkit-linear-gradient(120deg, #bd34fe 30%, #41d1ff);
    --vp-home-hero-image-background-image: linear-gradient(75deg, #bd34fe 40%, #47caff 50%);
    --vp-home-hero-image-filter: blur(60px) opacity(0.66);
}

@media (min-width: 640px) {
    body {
        --vp-home-hero-image-filter: blur(80px) opacity(0.66);
    }
}

@media (min-width: 960px) {
    body {
        --vp-home-hero-image-filter: blur(100px) opacity(0.66);
    }

    .VPHome .name .clip {
        line-height: 64px;
        font-size: 60px;
    }

    .VPHome .main .text {
        line-height: 64px;
        font-size: 57px;
    }
}

.VPHome .tagline a {
    color: var(--vp-c-brand-1);
    text-decoration: underline;
    text-underline-offset: 5px;
    transition: color 0.25s;
}

.VPHome .tagline a:hover {
    color: var(--vp-c-brand-2);
}

.VPHome article .details a {
    color: var(--vp-c-brand-1);
    text-decoration: underline;
    text-underline-offset: 3px;
    transition: color 0.25s;
}

.VPHome article .details a:hover {
    color: var(--vp-c-brand-2);
}

.VPHome .VPButton.medium.brand {
    position: relative;
    display: flex;
    align-items: center;
    padding-top: 5px;
    padding-bottom: 5px;
    padding-right: 15px;
    background-color: #3e63dd;
}

.VPHome .VPButton.medium.brand:hover {
    background-color: #5d83ff;
}

.VPHome .VPButton.medium.brand::after {
    content: "";
    mask: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='22' height='22' viewBox='0 0 24 24' fill='currentColor'%3E%3Cpath d='M17.92 11.62a1.001 1.001 0 0 0-.21-.33l-5-5a1.003 1.003 0 1 0-1.42 1.42l3.3 3.29H7a1 1 0 0 0 0 2h7.59l-3.3 3.29a1.002 1.002 0 0 0 .325 1.639 1 1 0 0 0 1.095-.219l5-5a1 1 0 0 0 .21-.33 1 1 0 0 0 0-.76Z'%3E%3C/path%3E%3C/svg%3E") no-repeat 50% 50%;
    /* Required to render correctly on mobile. */
    display: inline-block;
    width: 22px;
    height: 22px;
    padding-left: 30px;
    background-color: var(--vp-button-brand-text);
}

.VPHome .VPButton.medium.alt {
    position: relative;
    display: flex;
    align-items: center;
    padding-top: 5px;
    padding-bottom: 5px;
    padding-right: 15px;
}

.VPHome .VPButton.medium.alt::after {
    content: "";
    mask: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='20' height='20' viewBox='0 0 24 24' fill='currentColor'%3E%3Cpath d='M19.33 10.18a1 1 0 0 1-.77 0 1 1 0 0 1-.62-.93l.01-1.83-8.2 8.2a1 1 0 0 1-1.41-1.42l8.2-8.2H14.7a1 1 0 0 1 0-2h4.25a1 1 0 0 1 1 1v4.25a1 1 0 0 1-.62.93Z'%3E%3C/path%3E%3Cpath d='M11 4a1 1 0 1 1 0 2H7a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1v-4a1 1 0 1 1 2 0v4a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3h4Z'%3E%3C/path%3E%3C/svg%3E") no-repeat 50% 50%;
    /* Required to render correctly on mobile. */
    display: inline-block;
    width: 20px;
    height: 20px;
    padding-left: 32px;
    background-color: var(--vp-button-alt-text);
}
</style>

<style scoped>
/* A hack copying home page specific styles, as they're applied with guid attr. */
.features { position: relative; padding: 0 24px; }
@media (min-width: 640px) { .features { padding: 0 48px; } }
@media (min-width: 960px) { .features { padding: 0 64px; } }
.container { margin: 0 auto; max-width: 1152px; }
.items { display: flex; flex-wrap: wrap; }
.item { padding: 8px; width: 100%; }
@media (min-width: 640px) { .item.grid-4 { width: 50%; } }
@media (min-width: 768px) { .item.grid-4 { width: 50%; } .item.grid-3 { width: calc(100% / 3); } }
@media (min-width: 960px) { .item.grid-4 {width: 25%} }
.VPFeature { display: block; border: 1px solid var(--vp-c-bg-soft); border-radius: 12px; height: 100%;background-color: var(--vp-c-bg-soft); transition: border-color .25s, background-color .25s; }
.box { display: flex; flex-direction: column; padding: 24px; height: 100%; }
.box-title { display: flex; align-items: baseline; column-gap: 15px; }
.icon {display: flex; justify-content: center; align-items: center; margin-bottom: 20px; border-radius: 6px;background-color: var(--vp-c-default-soft); width: 40px; height: 40px; font-size: 22px; transition: background-color .25s; }
.title { line-height: 24px; font-size: 18px; font-weight: 600; }
.details { flex-grow: 1; line-height: 24px; font-size: 14px; font-weight: 500; color: var(--vp-c-text-2); }
</style>
# Introduction

## What?

Bootsharp is a solution for building web applications where the domain logic is authored in .NET C# and consumed by a standalone JavaScript or TypeScript project.

## Why?

C# is a popular choice for building maintainable software with complex domain logic, especially in enterprise and financial systems. However, its frontend capabilities are limited—particularly when compared to what the web ecosystem offers.

The web platform is the industry standard for modern UI development. Frameworks such as [React](https://react.dev) and [Svelte](https://svelte.dev) provide exceptional tooling, fast iteration, and a vast ecosystem, enabling developers to build high-quality interfaces with ease.

Solutions like [Blazor](https://dotnet.microsoft.com/en-us/apps/aspnet/web-apps/blazor) attempt to bring the entire web platform into .NET, effectively reversing the natural workflow and restricting access to native JavaScript tools. Bootsharp takes the opposite approach: it enables high-level interoperation between C# and TypeScript, so each layer can be developed within its optimal environment.

With Bootsharp, you implement domain logic in C# and build the UI using familiar web technologies — the interop layer is generated automatically with zero manual authoring. Your project can then be published to the web or bundled as a native desktop or mobile application using [Electron](https://electronjs.org) or [Tauri](https://tauri.app).

## How?

Bootsharp is installed as a [NuGet package](https://www.nuget.org/packages/Bootsharp) into the C# project dedicated to building the solution for the web. It is specifically designed not to "leak" the dependency outside the entry assembly of the web target—essential for keeping the domain clean of any platform-specific details.

While it's possible to author both export (C# → JS) and import (C# ← JS) bindings via static methods, complex solutions benefit from interface-based interop. Simply provide Bootsharp with C# interfaces describing the export and import API surfaces, and it will automatically generate the associated bindings and type declarations.

![](/img/banner.png)

Bootsharp will automatically build and bundle the JavaScript package when publishing the C# solution, and generate a `package.json`, allowing you to reference the entire C# solution as any other ES module in your web project.

::: code-group
```jsonc [package.json]
"scripts": {
    // Compile C# solution into ES module.
    "compile": "dotnet publish backend"
},
"dependencies": {
    // Reference C# solution module.
    "backend": "file:backend"
}
```
:::

::: code-group
```ts [main.ts]
// Import C# solution module.
import bootsharp, { Backend, Frontend } from "backend";

// Boot C# WASM module.
await bootsharp.boot();

// Subscribe to C# event.
Frontend.onUserChanged.subscribe(updateUserUI);

// Invoke C# method.
Backend.addUser({ name: "Carl" });
```
:::
# Getting Started

## Configure C# Project

In `.csproj` file, set wasm runtime identifier and reference Bootsharp package:

```xml

<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Bootsharp" Version="*-*"/>
    </ItemGroup>

</Project>
```

## Author Interop APIs

Specify interop surface in the C# project.

```cs
using System;
using Bootsharp;

public static partial class Program
{
    [Export] // Used in JS as Program.onMainInvoked.subscribe(..)
    public static event Action<string>? OnMainInvoked;

    public static void Main ()
    {
        OnMainInvoked?.Invoke($"Hello {GetFrontendName()}, .NET here!");
    }

    [Import] // Set in JS as Program.getFrontendName = () => ..
    public static partial string GetFrontendName ();

    [Export] // Invoked from JS as Program.GetBackendName()
    public static string GetBackendName () => Environment.Version;
}
```

::: info NOTE
Authoring interop via static methods is impractical for large API surfaces—it's shown here only as a simple way to get started. For real projects, consider using [modules](/guide/interop-modules) instead.
:::

## Compile ES Module

Run following command under the solution root:

```sh
dotnet publish
```

— which will produce a `bin/bootsharp` directory with the compiled module and a `package.json` next to the `.csproj`.

::: tip
When publishing in `Release` (default for `dotnet publish`), Bootsharp automatically enables the [NativeAOT-LLVM](/guide/llvm) compiler, speed-focused WASM optimization, aggressive trimming, and an extra Binaryen pass when `wasm-opt` is available.

Use the debug configuration (`dotnet publish -c Debug`) to disable optimizations and use the default .NET compiler for a better debugging experience and faster build times, at the cost of significantly increased bundle size and degraded runtime performance.
:::

## Consume C# APIs in JavaScript

Import the compiled ES module, assign imported functions, boot the runtime and use exported methods:

::: code-group

```js [JavaScript Runtime (Node, Deno, Bun)]
// Importing compiled ES module.
import bootsharp, { Program } from "./bin/bootsharp/index.mjs";

// Binding 'Program.GetFrontendName' import invoked in C#.
Program.getFrontendName = () => process.version;

// Subscribing to 'Program.OnMainInvoked' C# event.
Program.onMainInvoked.subscribe(console.log);

// Initializing dotnet runtime and invoking entry point.
await bootsharp.boot();

// Invoking 'Program.GetBackendName' C# method.
console.log(`Hello ${Program.getBackendName()}!`);
```

```html [Web Browser]
<!DOCTYPE html>

<script type="module">

    // Importing compiled ES module.
    import bootsharp, { Program } from "./bin/bootsharp/index.mjs";

    // Binding 'Program.GetFrontendName' import invoked in C#.
    Program.getFrontendName = () => "Browser";

    // Subscribing to 'Program.OnMainInvoked' C# event.
    Program.onMainInvoked.subscribe(console.log);

    // Initializing dotnet runtime and invoking entry point.
    await bootsharp.boot();

    // Invoking 'Program.GetBackendName' C# method.
    console.log(`Hello ${Program.getBackendName()}!`);

</script>
```

:::

## Run the App

Assuming the above code is in `main.mjs` file for JavaScript runtimes or in `index.html` file for browser, run the following to test the app:

::: code-group

```sh [Node]
node main.mjs
```

```sh [Deno]
deno run main.mjs
```

```sh [Bun]
bun main.mjs
```

```sh [Browser]
npx serve
```

:::

::: tip EXAMPLE
Find full sources of the minimal sample on GitHub: https://github.com/elringus/bootsharp/tree/main/samples/minimal.
:::
# Interop Modules

Instead of manually authoring a binding for each member, let Bootsharp generate them automatically using the `[Import]` and `[Export]` assembly attributes. The type listed under each attribute defines an *interop module*.

For example, say we have a JavaScript UI (frontend) with a setting stored on the JS side, and a C# domain layer (backend) that wants to expose state changes back to JavaScript. You can describe the imported frontend module like this:

```csharp
interface IFrontend
{
    bool IsMuted { get; set; }
}
```

Now, add the module type to the JS import list:

```csharp
[assembly: Import(typeof(IFrontend))]
```

Bootsharp will automatically implement the interface in C#, wiring it to JavaScript, while also providing you with a TypeScript spec to implement on the frontend:

```ts
export namespace Frontend {
    export let isMuted: boolean;
}
```

Imported modules must be interfaces, since Bootsharp generates the C# implementation that calls into JavaScript.

Now, define the backend contract to expose to JavaScript. An exported module can be either an interface or a non-static class — pick whichever fits your backend best:

```csharp
public interface IBackend
{
    event Action<Data> OnDataChanged;
    Data? Current { get; set; }
    void AddData (Data data);
}
```

```csharp
public class Backend
{
    public event Action<Data>? OnDataChanged;
    public Data? Current { get; set; }
    public void AddData (Data data) { /* ... */ }
}
```

Export the module to JavaScript:

```csharp
[assembly: Export(typeof(IBackend))]
// or
[assembly: Export(typeof(Backend))]
```

Either form produces the following spec to be consumed on the JavaScript side:

```ts
export namespace Backend {
    export const onDataChanged: EventSubscriber<[data: Data]>;
    export let current: Data | undefined;
    export function addData(data: Data): void;
}
```

Imported module events work the other way around: declare a real C# event on the interface, and Bootsharp will generate a JavaScript `EventBroadcaster` plus a regular subscribable event on the generated C# implementation.

To make Bootsharp automatically inject and initialize the generated interop implementations, use the [dependency injection](/guide/extensions/dependency-injection) extension.

::: tip Example
Find an example of using modules in the [React sample](https://github.com/elringus/bootsharp/tree/main/samples/react).
:::
# Interop Instances

When a type with a mutable semantic (a class or an interface) appears on the interop boundary or under a [serialized type](/guide/serialization), instead of serializing and copying it by value, Bootsharp will instead generate an instance binding and pass it by reference, eg:

```csharp
public interface IExported
{
    string Value { get; set; }
    string GetFromCSharp ();
}

public interface IImported
{
    string Value { get; set; }
    string GetFromJavaScript ();
}

public class Exported : IExported
{
    public string Value { get; set; } = "cs";
    public string GetFromCSharp () => "cs";
}

public static partial class Factory
{
    [Export] public static IExported GetExported () => new Exported();
    [Import] public static partial IImported GetImported ();
}

var imported = Factory.GetImported();
imported.GetFromJavaScript(); // returns "js"
imported.value = "updated"; // invokes the JS setter
_ = imported.value; // invokes the JS getter
```

```ts
import { Factory, IImported } from "bootsharp";

class Imported implements IImported {
    value = "js";
    getFromJavaScript() { return "js"; }
}

Factory.getImported = () => new Imported();

const exported = Factory.getExported();
exported.getFromCSharp(); // returns "cs"
exported.value = "updated"; // invokes the C# setter
_ = exported.value; // invokes the C# getter
```

::: info NOTE
Only user types are subject to instance binding. BCL types are ignored to prevent leaking the entire .NET runtime into the generated interop layer.
:::
# Type Declarations

Bootsharp will automatically generate [type declarations](https://www.typescriptlang.org/docs/handbook/2/type-declarations) for interop APIs when building the solution. One `.g.d.mts` file is emitted per C# namespace, colocated with the matching `.g.mjs` binding under the `generated/modules` directory of the compiled module package.

## Function Declarations

For interop methods, function declarations are emitted under the class's TS namespace wrapper inside the C# namespace's module:

```csharp
public class Class
{
    [Export]
    public static void Baz() { }
}
```

— will make the following emitted in `generated/modules/index.g.d.mts`:

```ts
export namespace Class {
    export function baz(): void;
}
```

— which allows consuming the API in JavaScript as:

```ts
import { Class } from "bootsharp";

Class.baz();
```

Imported methods will be emitted as properties, which have to be assigned before booting the runtime:

::: code-group

```csharp [Class.cs]
public partial class Class
{
    [Import]
    public static partial void Baz();
}
```

```ts [index.g.d.mts]
export namespace Class {
    export let baz: () => void;
}
```

```ts [main.ts]
import { Class } from "bootsharp";

Class.baz = () => {};
```

:::

## Overloaded Methods

JavaScript does not have function overloads, so Bootsharp automatically disambiguates them when projecting overloaded C# methods. The overload with the fewest parameters keeps the original name; the rest are suffixed with `With...` derived from the extra parameter names (or, when that is still ambiguous, from the full parameter names or parameter types).

::: code-group

```csharp [Class.cs]
public class Class
{
    [Export] public static void Start (string title) {}
    [Export] public static void Start (string title, string info) {}
    [Export] public static void Start (string title, double progress) {}
    [Export] public static void Start (string title, string info, double progress) {}
}
```

```ts [index.g.d.mts]
export namespace Class {
    export function start(title: string): void;
    export function startWithInfo(title: string, info: string): void;
    export function startWithProgress(title: string, progress: number): void;
    export function startWithInfoAndProgress(title: string, info: string, progress: number): void;
}
```

:::

## Generic Methods

JavaScript does not have generic functions, so Bootsharp projects an exported generic method as a concrete overload for each user type that satisfies the method's type parameter constraint, suffixing each with `Of...` derived from the bound type's name. Only a single type parameter, constrained to a user type, is expanded.

::: code-group

```csharp [Class.cs]
public interface IShape {}
public class Circle : IShape {}
public class Square : IShape {}

public class Class
{
    [Export]
    public static T CreateShape<T> () where T : IShape
    {
        if (typeof(T) == typeof(Circle)) return new Circle();
        if (typeof(T) == typeof(Square)) return new Square();
    }
}
```

```ts [index.g.d.mts]
export interface Circle {}
export interface Square {}

export namespace Class {
    export function createShapeOfCircle(): Circle;
    export function createShapeOfSquare(): Square;
}
```

:::

## Default Arguments

C# method parameters with default values are emitted as optional TypeScript parameters using the `?:` syntax, letting callers omit them at the call site:

::: code-group

```csharp [Class.cs]
public class Class
{
    [Export]
    public static void Greet (string name, string greeting = "Hello") {}
}
```

```ts [index.g.d.mts]
export namespace Class {
    export function greet(name: string, greeting?: string): void;
}
```

```ts [main.ts]
import { Class } from "bootsharp";

Class.greet("World");
Class.greet("World", "Hi");
```

:::

## Property Declarations

Exported properties are emitted as variables under the declaring class's TS namespace:

::: code-group

```csharp [Class.cs]
public class Class
{
    [Export]
    public static string Baz { get; set; } = "";
}
```

```ts [index.g.d.mts]
export namespace Class {
    export let baz: string;
}
```

```ts [main.ts]
import { Class } from "bootsharp";

Class.baz = "updated";
```

:::

Imported properties are emitted as accessor pairs, which have to be assigned before booting the runtime:

::: code-group

```csharp [Class.cs]
public static partial class Class
{
    [Import]
    public static partial string Baz { get; set; }
}
```

```ts [index.g.d.mts]
export namespace Class {
    export let baz: { get: () => string; set: (value: string) => void };
}
```

```ts [main.ts]
import { Class } from "bootsharp";

let baz = "";
Class.baz = { get: () => baz, set: value => baz = value };
```

:::

## Event Declarations

Exported events are emitted as `EventSubscriber` objects:

::: code-group

```csharp [Class.cs]
public class Class
{
    [Export]
    public static event Action<string>? OnBaz;
}
```

```ts [index.g.d.mts]
export namespace Class {
    export const onBaz: EventSubscriber<[payload: string]>;
}
```

```ts [main.ts]
import { Class } from "bootsharp";

Class.onBaz.subscribe(payload => {});
```

:::

Imported events are emitted as `EventBroadcaster` objects:

::: code-group

```csharp [Class.cs]
public static partial class Class
{
    [Import]
    public static event Action<string>? OnBaz;
}
```

```ts [index.g.d.mts]
export namespace Class {
    export const onBaz: EventBroadcaster<[payload: string]>;
}
```

```ts [main.ts]
import { Class } from "bootsharp";

Class.onBaz.broadcast("updated");
```

:::

## Delegate Declarations

Custom delegates are emitted as TypeScript function-type aliases:

::: code-group

```csharp [Class.cs]
public delegate void Notify (string msg);

public class Class
{
    [Export]
    public static Notify GetNotify () => msg => Console.WriteLine(msg);
}
```

```ts [index.g.d.mts]
export type Notify = (msg: string) => void;

export namespace Class {
    export function getNotify(): Notify;
}
```

```ts [main.ts]
import { Class } from "bootsharp";

const notify = Class.getNotify();
notify("hello");
```

:::

Built-in `System.Action` and `System.Func` variants are supported as well:

::: code-group

```csharp [Class.cs]
public class Class
{
    [Export] public static Action<string>? Logger { get; set; }
}
```

```ts [index.g.d.mts]
export namespace Class {
    export let logger: system.Action<string> | undefined;
}
```

```ts [main.ts]
import { Class } from "bootsharp";

Class.logger = msg => console.log(msg);
Class.logger("hello");
```

:::

## Documentation Declarations

When an inspected assembly has XML documentation generated, Bootsharp mirrors the matching documentation into the emitted TypeScript declarations.

::: code-group

```csharp [MathApi.cs]
/// <summary>Math API.</summary>
public class MathApi
{
    /// <summary>Adds two numbers.</summary>
    /// <param name="left">Left number.</param>
    /// <param name="right">Right number.</param>
    /// <returns>The sum.</returns>
    [Export]
    public static int Add (int left, int right) => left + right;
}
```

```ts [index.g.d.mts]
/**
 * Math API.
 */
export namespace MathApi {
    /**
     * Adds two numbers.
     * @param left Left number.
     * @param right Right number.
     * @returns The sum.
     */
    export function add(left: number, right: number): number;
}
```

:::

## Nullability

Bootsharp uses different TypeScript nullish forms depending on where a nullable C# value appears:

- nullable method arguments become `| undefined`
- nullable properties become optional with `?`
- nullable return values become `| null`
- nullable collection elements and dictionary values become `| null`

This is intentional and optimized for TypeScript ergonomics: `undefined` fits omitted or optional inputs, while `null` fits explicit data crossing the interop boundary.

## Namespaces

Members declared inside a C# namespace are emitted into a module path derived from that namespace: dots become path separators and casing is lower-kebab-cased. Members without a namespace land in the default `index` module (as shown in the examples above).

::: code-group

```csharp [Class.cs]
namespace Foo.Bar;

public class Class
{
    [Export]
    public static void Baz () { }
}
```

```ts [main.ts]
import { Class } from "bootsharp/foo/bar";

Class.baz();
```

:::

You can control how the C#-side namespace and type names resolve to the generated module and node names with the [rename attributes](/guide/renaming).

## Configuring Type Mappings

You can rename or omit the JavaScript node generated for an associated C# type via the [rename attributes](/guide/renaming).
# Serialization

Most simple types, such as numbers, booleans, strings, arrays (lists) and promises (tasks) of them are marshalled in-memory when crossing the C# <-> JavaScript boundary. Below are some of the natively-supported types (refer to .NET docs for the [full list](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/import-export-interop)):

| C#       | JavaScript | Task of | Array of |
|----------|------------|:-------:|:--------:|
| bool     | boolean    |   ✔️    |    ❌     |
| byte     | number     |   ✔️    |    ✔️    |
| char     | string     |   ✔️    |    ❌     |
| string   | string     |   ✔️    |    ✔️    |
| int      | number     |   ✔️    |    ✔️    |
| long     | BigInt     |   ✔️    |    ❌     |
| float    | Number     |   ✔️    |    ❌     |
| DateTime | Date       |   ✔️    |    ❌     |

When a value of non-natively supported type is specified in an interop API, Bootsharp will de-/serialize it using a custom efficient binary serialization format. The whole process is encapsulated under the hood on both the C# and JavaScript sides, so you don't have to manually author generator hints or specify `[MarshallAs]` attributes for each value:

```csharp
public record User (long Id, string Name, DateTime Registered);

[Export]
public static void AddUser (User user) { }

[Export]
public static event Action<User>? OnUserModified;
```

— Bootsharp will automatically emit C# and JavaScript code required to de-/serialize `User` record on both ends, so that you can consume the APIs as if they were initially authored in JavaScript:

```ts
import { Program } from "bootsharp";

Program.addUser({ id: 17, name: "Carl", registered: Date.now() });

Program.onUserModified.subscribe(handleUserModified);

function handleUserModified(user: Program.User) { }
```

::: info NOTE
Only types with immutable semantics (structs, records, and read-only collections) are subject to serialization—other types are considered mutable and are passed by reference as [interop instances](/guide/interop-instances).
:::

## Enums Serialization

Enums are marshalled as numbers for better performance, while additional name <-> index mappings are emitted on the JavaScript side for convenience.

```csharp
public enum Options { Foo, Bar }

[Export]
public static Options GetOption () => Options.Bar;
```

— while "GetOptions" return value will be passed to JavaScript as an integer index, Bootsharp will map enum indexes to string values (and vice-versa) in the emitted code, so that following will work as expected:

```ts
import { Program } from "bootsharp";

const option = Program.getOption();
console.log(option === Program.Options.Foo); // false
console.log(option === Program.Options.Bar); // true
console.log(Program.Options[Program.Options.Foo]); // "Foo"
console.log(Program.Options[1]); // "Bar"
```

## Dictionary Serialization

Bootsharp marshals C# dictionaries as ES6 [Map](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Map):

```csharp
[Export]
public static Dictionary<string, bool> GetMap () =>
    new () { ["foo"] = true, ["bar"] = false };
```

— the dictionary can be accessed with standard `Map` APIs:

```ts
import { Program } from "bootsharp";

const map = Program.getMap();
console.log(map.get("foo")); // true
console.log(map.get("bar")); // false
```

## Collection Interfaces

It's common to use various collection interfaces, such as `IReadOnlyList` or `IReadOnlyDictionary` when authoring C# APIs. Bootsharp will accept any kind of array or dictionary compatible interface in the interop APIs and marshal them as plain arrays and maps by default:

```csharp
[Export]
public static IReadOnlyDictionary<string, float> Map (
    IReadOnlyList<string> a, IReadOnlyCollection<float> b) { }
```

```ts
import { Program } from "bootsharp";

const map = Program.map(["foo", "bar"], [0, 7]);
console.log(map.get("bar")); // 7
```
# Specialization

Bootsharp marshals every type automatically based on the convention: types with immutable/value semantics are [serialized by value](/guide/serialization), and others are [passed by reference](/guide/interop-instances).

It's possible to customize the behaviour with **specialization** are redefine how a particular CLR type crosses the interop boundary and what surface it exposes on the other side.

## How It Works

A specialization is a pair of classes describing a custom interop surface for a specific CLR type — one for each direction:

- **Export** (C# → JS) — a class annotated with `[SpecializeExport(typeof(T))]` and inherited from `SpecializedExport`. Bootsharp wraps an exported instance of the specialized type into this class before it crosses to JavaScript.
- **Import** (JS → C#) — an abstract class annotated with `[SpecializeImport(typeof(T))]` and inherited from `SpecializedImport`. Bootsharp uses it as the base of the generated interop proxy and treats its abstract members as the interop surface wired to JavaScript.

The two halves are paired: the export half implements every abstract member declared on the import half, so the same shape is exposed in both directions.

To override how Bootsharp marshals a type declare a specialization pair with the same attributes. Here's an example for `IComparer<T>`:

```csharp
[SpecializeImport(typeof(IComparer<>))]
public abstract class ComparerImport<T> (int id)
    : SpecializedImport(id), IComparer<T>
{
    public abstract int Compare (T? x, T? y);
}

[SpecializeExport(typeof(IComparer<>))]
public class ComparerExport<T> (IComparer<T> cmp)
    : SpecializedExport(cmp)
{
    public int Compare (T? x, T? y) => cmp.Compare(x, y);
}
```

The import half declares the interop surface (`Compare`) as abstract members; Bootsharp generates a proxy that forwards them to JavaScript and, since the class implements `IComparer<T>`, the proxy is usable as one on the C# side.

On the JavaScript side a comparer is just an object matching the declared surface:

```ts
import { Program } from "bootsharp";

Program.provideComparer = () => ({
    compare: (x, y) => x < y ? -1 : x > y ? 1 : 0
});

const comparer = Program.getComparer();
comparer.compare("a", "b"); // -1
```

::: tip
When the specialized `Clr` type is a class, it will also affect (specialize) any subclasses discovered on the interop surfaces.
:::

## Injecting Code

The `[SpecializeImport]` attribute accepts optional `CS`, `JS`, `JSCtor` and `Decl` snippets that are spliced verbatim into the generated C# or JavaScript proxies and its TypeScript declaration. This lets the imported proxy satisfy JS-side contracts that aren't expressible through the C# abstract members alone — for example, injecting an iterator:

```csharp
[SpecializeImport(typeof(ICustomCollection<>),
    JS: "[Symbol.iterator]() { return this.copy()[Symbol.iterator](); }",
    Decl: "[Symbol.iterator](): IterableIterator<T>;")]
```

The `CS` snippet can contain `$full` markers — they will be replaced with the fully-qualified type name of the specialized instance. This allows referencing the concrete specialized instances in the proxy when the specialization is applied to a base class.

The `Decl` snippet can contain `$full`, `$name` and `$T{I}` markers — first is the same as CS one, but in TypeScript context, name is the short type name and `T` is the fully-qualified name of the generic type argument with the `{I}` inde (if any), for example `$T{0}` is replaced with the first generic argument.

When `Decl` value starts with `export ` — the content will replace the entire TypeScript declaration of the type, instead of splicing it into the bottom of the default type declaration.

::: tip EXAMPLE
Find a more advanced example of injecting C# and JS constructor code to synthesise property events in the [E2E test project](https://github.com/elringus/bootsharp/tree/main/src/js/test/cs/Test.Library/Specialization.cs).
:::

## Unwrapping

An import specializer normally *is* the value handed to C# — the generated proxy implements the specialized interface, so it can stand in for it directly (the way `ComparerImport<T>` above serves as an `IComparer<T>`).

That doesn't work when the specialized type can't be implemented by a proxy, such as a value type like `CancellationToken`. In that case the proxy exposes the JavaScript-side surface as abstract members and overrides `SpecializedImport.Unwrap()` to build the concrete value from them:

```csharp
[SpecializeImport(typeof(CancellationToken))]
public abstract class CancellationTokenImport (int id) : SpecializedImport(id)
{
    public abstract bool IsCancellationRequested { get; }
    public abstract event Action OnCancellationRequested;

    private CancellationTokenSource? src;

    protected internal override object Unwrap ()
    {
        if (src != null) return src.Token;
        src = new();
        if (IsCancellationRequested) src.Cancel();
        else OnCancellationRequested += src.Cancel;
        return src.Token;
    }
}

[SpecializeExport(typeof(CancellationToken))]
public sealed class CancellationTokenExport : SpecializedExport
{
    public bool IsCancellationRequested => ct.IsCancellationRequested;
    public event Action? OnCancellationRequested;

    private readonly CancellationToken ct;

    public CancellationTokenExport (CancellationToken ct) : base(ct)
    {
        this.ct = ct;
        ct.Register(() => OnCancellationRequested?.Invoke());
    }
}
```

Bootsharp calls `Unwrap()` to obtain the value passed to C# — here a real `CancellationToken` backed by a source that's cancelled whenever the JavaScript token reports cancellation. The paired JavaScript class signals through the same surface:

```ts
import { CancellationToken } from "bootsharp";

const token = new CancellationToken();
token.cancel(); // fires onCancellationRequested
```

## Reference

The built-in specializations live in [`Specialized.cs`](https://github.com/elringus/bootsharp/blob/main/src/cs/Bootsharp.Common/Specialization/Specialized.cs); their JavaScript counterparts are the modules under [`src/js/src/bcl`](https://github.com/elringus/bootsharp/tree/main/src/js/src/bcl). Use them as a template when authoring your own.
# Renaming

By default, Bootsharp derives JavaScript names from your C# types: a type's namespace becomes the module path, the type name becomes the node (object) under that module, and members are `camelCased`.

It's possible to customize the behaviour by specifying static methods annotated with `[RenameModule]`, `[RenameNode]` and `[RenameMember]` attributes. The methods receive a CLR type associated with the renamed artifact plus the default name generated by Bootsharp and expected to return the custom name you want to use.

## Module

`[RenameModule]` customizes the module path that groups the generated bindings and declarations. The default is the slugified C# namespace, or `index` for global types. Returning an empty, null or whitespace string falls back to the default `index` module.

```cs
[RenameModule]
public static string RenameModule (Type type, string @default) =>
    @default.Replace("/foo/bar", "/foo");
```

In the example above we fold `/foo/bar` modules into the `/foo` module.

## Node

`[RenameNode]` customizes the node — the object representing a C# type under its module. The default is the reflected type name. Returning an empty, null or whitespace string **erases** the type, omitting it from the generated JavaScript.

```cs
[RenameNode]
public static string RenameNode (Type type, string @default)
{
    if (type.Name == "Foo") return null;
    if (type.IsInterface && @default.EndsWith("UI")) return @default[1..^2];
    if (type.IsInterface && @default.StartsWith('I')) return @default[1..];
    return @default;
}
```

The example removes `Foo` types from the interop surface, strips the leading `I` from interface names and drops a trailing `UI` suffix when present.

## Member

`[RenameMember]` customizes member names — the methods, properties and events projected on an interop surface. The default is the `camelCased` (and disambiguated) member name. Returning an empty, null or whitespace string **erases** the member, omitting it from the generated JavaScript.

```cs
[RenameMember]
public static string RenameMember (MemberInfo info, string @default)
{
    if (info.DeclaringType.Name == "Foo")
        if (info is EventInfo) return null;
        else return char.ToUpperInvariant(@default[0]) + @default[0..];
    return @default;
}
```

Here we drop all events and rename other members declared under `Foo` to `PascalCase`.

## Combining Renamers

Define any combination of the three renamers — each is optional and resolved independently. The example below groups everything under an `api` module, removes the interface prefixes and drops properties from all surfaces:

```cs
[RenameModule]
public static string Module (Type type, string @default) => "api";

[RenameNode]
public static string Node (Type type, string @default) =>
    type.IsInterface ? @default[1..] : @default;

[RenameMember]
public static string Member (MemberInfo info, string @default) =>
    info is PropertyInfo ? null : @default;
```
# Build Configuration

Build and publish related options are configured in `.csproj` file via MSBuild properties.

| Property                   | Default          | Description                                                                       |
|----------------------------|------------------|-----------------------------------------------------------------------------------|
| BootsharpName              | bootsharp        | Name of the generated JavaScript package and WASM binary. |
| BootsharpPublishDirectory  | /bin/bootsharp   | Directory to publish generated JavaScript module.                                 |
| BootsharpBinariesDirectory | (empty)          | Directory to publish binaries; when empty, binaries are embedded (see [Sideloading](sideloading)). |
| BootsharpPackageDirectory  | project-dir      | Directory to publish `package.json` file.                                         |

Below is an example configuration, which will make Bootsharp name the compiled module "backend" (instead of the default "bootsharp"), publish the `package.json` under the solution directory root and emit the runtime binaries into a "public/bin" directory one level above the solution root:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
        <BootsharpName>backend</BootsharpName>
        <BootsharpPackageDirectory>$(SolutionDir)</BootsharpPackageDirectory>
        <BootsharpBinariesDirectory>$(SolutionDir)../public/bin</BootsharpBinariesDirectory>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Bootsharp" Version="*-*"/>
    </ItemGroup>

</Project>
```

## Globalization

By default, Bootsharp disables .NET globalization on WASM. This keeps the published output smaller, but culture-specific formatting and culture construction will use invariant mode.

To enable globalization, explicitly disable invariant globalization in your project file:

```xml
<PropertyGroup>
    <InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

When invariant globalization is disabled, Bootsharp will automatically include the ICU files emitted by the .NET WASM build and configure the runtime accordingly.

Bootsharp supports the following globalization modes:

| Mode    | How to enable                                                        | Behavior                                                                                  |
|---------|----------------------------------------------------------------------|-------------------------------------------------------------------------------------------|
| Sharded | Didable `InvariantGlobalization`                                     | Publishes the default sharded ICU files (`icudt_*.dat`).                                  |
| Full    | Didable `InvariantGlobalization` and enable `WasmIncludeFullIcuData` | Publishes the full ICU data file (`icudt.dat`) and supports many cultures in one runtime. |
# Sideloading Binaries

By default, Bootsharp embeds the .NET WASM runtime and the solution's assemblies into the generated JavaScript module as base64 strings. This is convenient — `bootsharp.boot()` works with no arguments and no extra files to serve. The trade-off is roughly 30% extra bundle size due to the base64 encoding.

To disable embedding, set `BootsharpBinariesDirectory` to the directory where the binaries should be published:

```xml
<PropertyGroup>
    <BootsharpBinariesDirectory>public</BootsharpBinariesDirectory>
</PropertyGroup>
```

The compiled WASM module, solution assemblies, ICU data and (in debug builds) debug symbols will be emitted to that directory as separate files instead of being inlined into the module. You then have two ways to feed them to `boot`:

Pass a root URL to fetch the resources from at runtime:

```ts
// Assuming the binaries are served from "/public" under the website root.
await bootsharp.boot("/public");
```

Or load the binaries yourself and pass them as a `BootResources` object:

```ts
import { readFileSync } from "node:fs";

const wasm = readFileSync("public/bootsharp.wasm");
await bootsharp.boot({ wasm });
```

This way the binary files can be streamed directly from the server, cached separately, or loaded from any source you control — useful for trimming initial bundle size or sharing the runtime across multiple modules.

::: tip EXAMPLE
Find sideloading example in the [trimming sample](https://github.com/elringus/bootsharp/blob/main/samples/trimming).
:::
# NativeAOT-LLVM

Starting with v0.6.0 Bootsharp supports .NET's experimental [NativeAOT-LLVM](https://github.com/dotnet/runtimelab/tree/feature/NativeAOT-LLVM) backend.

By default, when targeting `browser-wasm`, .NET is using the Mono runtime, even when compiled in AOT mode. Compared to the modern NativeAOT (previously CoreRT) runtime, Mono's performance is lacking in speed, binary size and compilation times. NativeAOT-LLVM backend not only uses the modern runtime instead of Mono, but also optimizes it with the [LLVM](https://llvm.org) toolchain, further improving the performance.

Below is a benchmark comparing interop and compute performance of various languages and .NET versions compiled to WASM to give you a rough idea on the differences:

![](/img/llvm-bench.png)

— sources of the benchmark are here: https://github.com/elringus/bootsharp/tree/main/samples/bench.

## Setup

Starting with Bootsharp 0.8.0 no extra project configuration is required.

When publishing a Bootsharp project in `Release`, Bootsharp automatically enables the NativeAOT-LLVM toolchain, speed-focused code generation, and the trimming settings required by the LLVM backend.

## Binaryen

Bootsharp always tries to run Binaryen on release publishes with speed optimization enabled:

1. Install Binaryen: https://github.com/WebAssembly/binaryen/releases
2. Make sure `wasm-opt` is in the system path
3. If the tool is missing, Bootsharp will log a warning and continue with a non-fully-optimized WASM binary
# Dependency Injection

When using [modules](/guide/interop-modules), it's convenient to use a dependency injection mechanism to automatically route generated module implementations for the services that needs them.

Reference `Bootsharp.Inject` extension in the project configuration:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net9.0</TargetFramework>
        <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Bootsharp" Version="*-*"/>
        <PackageReference Include="Bootsharp.Inject" Version="*-*"/>
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="*"/>
    </ItemGroup>

</Project>
```

— and use `AddBootsharp` extension method to inject the generated import implementations; `RunBootsharp` will initialize generated export implementation by requiring the handlers, which should be added to the services collection before.

```csharp
using Bootsharp;
using Bootsharp.Inject;
using Microsoft.Extensions.DependencyInjection;

[assembly: Export(
    typeof(IExported),
    // other APIs to export to JavaScript
)]

[assembly: Import(
    typeof(IImported),
    // other APIs to import from JavaScript
)]

new ServiceCollection()
    // Inject generated implementation of IImported.
    .AddBootsharp()
    // Inject other services, which may require IImported.
    .AddSingleton<SomeService>()
    // Provide handler for the exported interface.
    .AddSingleton<IExported, Exported>()
    // Build the collection.
    .BuildServiceProvider()
    // Initialize the exported implementations.
    .RunBootsharp();
```

`IImported` can now be requested via .NET's DI, while `IExported` APIs are available in JavaScript:

```csharp
public class SomeService (IImported imported) { }
```

```ts
import { Exported } from "bootsharp";
```

::: tip EXAMPLE
Find example on using the DI extension in the [React sample](https://github.com/elringus/bootsharp/blob/main/samples/react).
:::
# File System

::: danger SPONSORS
This extension is exclusive for sponsors: https://github.com/sponsors/elringus.
:::

With the new [File System Access](https://developer.mozilla.org/en-US/docs/Web/API/File_System_API) APIs it's possible to access local file system directly from web browser. Bootsharp.FileSystem extension provides C# bindings and JavaScript package to use the APIs directly from C#.

Install the NuGet package to C# project:

```xml

<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net9.0</TargetFramework>
        <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Bootsharp" Version="*-*"/>
        <PackageReference Include="Bootsharp.FileSystem" Version="*-*"/>
    </ItemGroup>

</Project>
```

And the NPM package to JavaScript project:

```json
{
    "dependencies": {
        "backend": "file:backed",
        "@rewaffle/bootsharp-file-system": "latest"
    }
}
```

Before booting C# solution in JavaScript, initialize the file system extension:

```ts
import bootsharp, { Bootsharp } from "backend";
import * as fs from "@rewaffle/bootsharp-file-system";

fs.init(Bootsharp.FileSystem.FileMounter);
await bootsharp.boot();
```

Then proceed to C# where `IFileMounter` interface will be automatically injected by the extension importing following APIs from JavaScript:

```csharp
interface IFileMounter
{
    Task<string?> PickRoot (PickOptions? options = null);
    Task<IFileSystem> Mount (string root, IFileWatcher watcher);
    Task Unmount (string root);
}
```

Invoking `PickRoot` method will prompt user to select root directory to mount. It will return unique root directory identifier to be used with `Mount` and `Unmount` methods or `null` in case user cancelled pick dialogue. Optional `PickOptions` argument allows specifying which directory pick dialogue should start in, whether write access should be requested, etc.

After user picked a directory and you get the root ID, invoke `Mount`, which will return `IFileSystem` instance, providing common IO interface over the contents of the mapped directory:

```csharp
interface IFileSystem
{
    Task CreateDirectory (string uri);
    Task RemoveDirectory (string uri);
    Task WriteFile (string uri, byte[] content);
    Task DeleteFile (string uri);
    Task<byte[]> ReadFile (string uri);
    Task<FileInfo> GetFileInfo (string uri);
}
```

File watcher instance specified when invoking `Mount` allows handling file changes under the mapped directory:

```csharp
interface IFileWatcher
{
    Task HandleFileChanges (FileChange[] changes);
}
```

— until the directory is un-mounted, the watcher will be notified when an entry (directory or file) is added, removed or modified.

::: tip EXAMPLE
Find sample application built with `Bootsharp.FileSystem` in the [sponsors repository](https://github.com/rewaffle/extra).
:::
