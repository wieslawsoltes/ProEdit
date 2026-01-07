# Agent Instructions

- Prefer Span<T>, ReadOnlySpan<T>, Memory<T>, ArrayPool<T>, and other allocation-free .NET APIs wherever practical and safe.
- Follow SOLID and modern design practices; keep code encapsulated, composable, and easy to unit test.
- Keep core logic independent of SkiaSharp and Avalonia. When dependencies are necessary, introduce fast, minimal interfaces and keep adapters at the edges.
- Ensure the rendering system is fully pluggable and easy to swap. The default renderer is SkiaSharp-based, but the core must remain renderer-agnostic.
- Fully abstract editing and input handling (pointer, keyboard, gestures, etc.) behind high-performance contracts to keep the core decoupled and testable.
