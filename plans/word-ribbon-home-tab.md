# Word Ribbon Home Tab: UI/UX Analysis and Implementation Plan

## Progress
- [x] 1. Tune tab strip baseline metrics (strip color, underline emphasis, padding) to better match Word.
- [x] 2. Extract ribbon theme tokens into a dedicated Word Ribbon theme file (colors, typography, spacing, metrics).
- [x] 3. Implement group container and header templates (group border, label row, launcher placement, separators).
- [x] 4. Implement button family templates (large, small, split, dropdown, toggle) with Word-like hit targets.
- [x] 5. Implement input controls (combo box, spinner) and galleries (styles gallery, popup, footer actions).
- [x] 6. Implement overflow and collapsed group behavior with Word-like popup visuals and scaling. (Threshold scaling applied; visual QA on narrow widths still needed.)
- [x] 7. Implement QAT, keytips, and rich tooltips (title, description, shortcuts).
- [x] 8. Apply contextual tab set banding and accent handling.
- [x] 9. Polish states, accessibility, and animations; validate resizing behavior. (Focus/transition polish added; resize QA still pending.)

## Granular UI and UX Analysis (Home Tab)

### Global layout and surfaces
- The ribbon is a three-layer stack: title/quick access row, tab strip row, ribbon content row.
- Tab strip is a light gray surface with a thin bottom keyline that separates it from the ribbon content.
- Ribbon content surface is white with subtle border and rounded corners at the bottom only.
- Groups are separated by thin vertical keylines; no heavy boxes around groups.
- Group header labels sit centered at the bottom of each group with a small launcher glyph at the right edge.

### Tab strip
- Tab labels are small (around 12px) and spaced with a modest gap; active tab uses a bold weight.
- Active tab is not boxed; it is indicated by a thin colored underline.
- Hover state uses a very subtle background wash; selection uses underline, not background.
- Contextual tab sets show a colored label above the tab text (small, uppercase-like tone).

### Group containers and layout rhythm
- Each group is a fixed-height column aligned to a shared baseline.
- Groups align controls to a 2-3 row grid, where large controls can span two rows.
- The group header row is short and visually quieter than the control area.
- A dedicated dialog launcher sits at the bottom-right corner of the group header row.

### Control families
- Large button: 32-40px icon area with label under it; often used for Paste and similar.
- Small button: 16px icon with optional short text; used for most formatting actions.
- Split button: main action on the left, drop-down chevron on the right with a divider.
- Dropdown button: content + chevron in a single hit target; label left, chevron right.
- Toggle button: same as small button but shows a pressed state with subtle fill.
- Combo boxes: flat, compact height with a small chevron; used for font name and size.
- Spinner: compact text box with two stacked arrows on the right.
- Gallery: horizontal strip of style tiles, showing live previews with a drop-down to open a grid.

### Typography
- Tabs: 12px, normal weight; selected tab: semibold.
- Group headers: 10px, muted gray, centered.
- Button labels: 11-12px; large buttons wrap to two lines if needed.
- Combo box text is slightly tighter, 11-12px.

### Iconography
- Small icons are around 16px and align on a shared baseline.
- Large icons are around 32px and sit in a square hit area.
- Chevron and launcher glyphs are smaller and lighter, around 10-11px.

### Interaction and states
- Pointer over: subtle blue-tinted wash on buttons and tabs.
- Pressed: slightly darker wash and border.
- Checked: same as pressed with persistent fill.
- Disabled: reduced opacity and muted text.
- Keyboard focus: thin focus outline, not overly bright.
- Keytips: small, yellow badges with bold letters.

### Responsiveness and overflow
- As width shrinks, groups collapse into single buttons that open a group popup.
- Galleries shrink to a smaller strip with a drop-down expansion.
- Dialog launchers remain visible even when groups are collapsed.

## Custom Templated Control Inventory

- RibbonRootHost (surface, tab strip, content host, QAT integration)
- RibbonTabStrip / RibbonTabItem (underline selection, contextual label row)
- RibbonGroupContainer (border, separator, header row)
- RibbonGroupHeader (label + dialog launcher)
- RibbonLargeButton
- RibbonSmallButton
- RibbonSplitButton / RibbonSplitToggleButton
- RibbonDropDownButton
- RibbonToggleButton
- RibbonComboBox
- RibbonSpinner
- RibbonGallery + RibbonGalleryItem (inline + popup grid + footer actions)
- RibbonColorSplitButton
- RibbonGroupOverflowButton
- RibbonQuickAccessToolbar
- RibbonKeyTip
- RibbonToolTip (title, description, shortcut)
- RibbonContextualTabSetBand

## Implementation Plan

### Phase 1: Theme scaffolding and tab strip
- Create a Word Ribbon theme dictionary that mirrors FluentTheme structure.
  - Follow `Avalonia.Themes.Fluent/FluentTheme.xaml` and `Controls/FluentControls.xaml` patterns.
  - Define tokens for colors, typography, sizing, spacing, keylines, corner radii, and animation durations.
- Move ribbon colors and sizing tokens out of `RibbonControl.axaml` into the new dictionary.
- Apply the new theme include in `App.axaml` and wire `RibbonControl` to use the tokens.
- Update tab strip and tab item templates to match Word (underline selection, subtle hover).

### Phase 2: Group containers and layout grid
- Create a templated group container style with a consistent header row and separator.
- Add a `RibbonGroupHeader` template for label + launcher glyph with proper alignment.
- Refine `RibbonGroupPanel` metrics (row height, column width, spacing) to match the Word grid.
- Ensure collapsed group popup uses the same header + launcher template.

### Phase 3: Core control family templates
- Implement large and small button templates with consistent icon and label alignment.
- Implement split buttons and dropdown buttons with proper dividers and chevron placement.
- Standardize toggle states to match Word pressed/checked visuals.
- Add compact combo box and spinner templates aligned to the ribbon grid.

### Phase 4: Galleries and advanced controls
- Rebuild the styles gallery visuals to match Word tile sizing, typography, and spacing.
- Add popup grid metrics and footer actions styling consistent with Word.
- Introduce a compact color split button template with underline swatch.

### Phase 5: Overflow, QAT, and contextual sets
- Implement a Word-like QAT container with small buttons and minimal chrome.
- Implement keytips overlay positioning and styling.
- Implement contextual tab set banding using `RibbonContextualTabSet.AccentKey`.
- Improve group overflow button visuals and popup alignment.

### Phase 6: Interaction polish and accessibility
- Add subtle animations for hover/press and gallery open/close.
- Ensure consistent focus outlines and keyboard navigation ordering.
- Validate contrast, hit targets, and screen reader labels.
- Verify resizing behavior across narrow and wide window sizes.

## Notes
- Use Avalonia `ControlTheme` and templated controls so ribbon visuals can be swapped without changing core ribbon models.
- Keep all UI changes inside `Vibe.Office.Ribbon.Avalonia` and `Vibe.Word.App` to preserve core renderer-agnostic design.
