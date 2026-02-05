# Contributing

## Engineering Rules
- Follow SOLID and strict MVVM layering
- Views are passive and contain no code-behind logic beyond `InitializeComponent()`
- ViewModels inherit from `ReactiveObject` and use `ReactiveCommand`
- Prefer composition and interfaces over inheritance
- Avoid reflection and prefer source generators

## Avalonia Guidelines
- Use XAML for layout and visuals
- Use compiled bindings with explicit `x:DataType`
- Use `DataTemplate` or a custom `ViewLocator` for view lookup

## Performance
- Avoid LINQ in hot paths
- Prefer `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, and `ArrayPool<T>`
- Profile before and after optimization work

## Testing
- All production code requires unit tests using xUnit
- UI tests must use Avalonia Headless with xUnit integration

## Packages
- Package versions are centrally managed in `Directory.Packages.props`
