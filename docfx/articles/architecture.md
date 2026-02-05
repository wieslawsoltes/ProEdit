# Architecture

## Layers
- UI: Avalonia Views and XAML layouts only
- Presentation: ViewModels with state, commands, and reactive composition
- Domain: document models, layout, rendering, parsing, validation
- Infrastructure: file system, persistence, external integrations

## MVVM and ReactiveUI
- Views are passive and contain no code-behind logic beyond `InitializeComponent()`
- All inputs flow through bindings, commands, and behaviors
- ViewModels inherit from `ReactiveObject`
- Commands use `ReactiveCommand`
- Derived state uses `WhenAnyValue` and `ObservableAsPropertyHelper`
- Dialogs and user flows use `Interaction<TIn, TOut>`

## Avalonia Practices
- XAML for visuals and layout
- Compiled bindings only with explicit `x:DataType`
- Use `DataTemplate` or a custom `ViewLocator` for view lookup
- Prefer `DirectProperty` unless styling requires `StyledProperty`

## Performance Guidelines
- Prefer allocation-free APIs like `Span<T>`, `ReadOnlySpan<T>`, and `ArrayPool<T>`
- Avoid LINQ in hot paths
- Profile before and after performance changes

Next: [Build and Test](build-and-test.md)
