# Timbo Jimbo - UI

UI for UGUI in one package: components that draw, a layout engine whose nodes move on springs, and variants switched in place.

📐 **Layout**

A layout engine modelled on [Clay](https://github.com/nicbarker/clay): fit, grow and fixed sizing, wrapping and grids, floating, the safe area, and scrolling as a UIScrollView's. See [Layout](#layout).

🌀 **Motion**

Every change made inside `MotionSystem.Animate` springs from where things are drawn, as SwiftUI's `withAnimation`: layout, show and hide effects, matched names that fly between places, and values of your own. See [Motion](#motion).

🎛️ **Variants**

Looks kept inside a prefab and switched in place (Success, Warning, Error), recorded by editing, previewed without saving, driven by states and breakpoints, and passed down the hierarchy. See [Variants](#variants).

🎮 **Focus**

Keyboard and gamepad focus that only lands on what can be seen and used, moves by where things are drawn, stays inside a modal sheet, comes back when what had it goes, follows Tab, routes Cancel and Android's Back, and draws an indicator that flies to what has focus, only after keyboard or gamepad input. See [Focus](#focus).

🟦 **Box**

A `Graphic` that draws a rounded box as a signed distance field: per-corner radii, a continuous corner curvature (circle → parabola → cosine → cubic), an inset stroke, a blur radius and an offset. One instance is one effect, so a panel, its drop shadow and its border are three sibling objects.

🖼️ **Img**

An `Image` subclass with the same gradient and blend mode options, plus a choice of how the colour meets the sprite (multiply, replace, add, screen, darken, lighten, overlay, difference) and a single-pass blur. Everything Image does (sprite types, preserve aspect, fill, sprite meshes, raycasting, layout) works unchanged.

🌈 **Gradient Tint**

Fill a box with a three-stop gradient (from, via, to), or tint a sprite with a two-stop one, at any angle, with per-stop colour and alpha, interpolated in RGB, HSV, OkLab or OkLCh to match the [Property Bindings](https://github.com/Timbo-Jimbo/PropertyBindings) package.

⚡ **One Draw Call**

Every parameter lives in vertex data and all instances of a component share a single material, so shadows, panels, borders and icons batch together.

🎨 **Blend Modes**

Normal, Additive, Multiply and Screen. Each mode has its own shared material, so same-mode elements still batch; the material inspector shows a friendly Blend Mode dropdown.

🧩 **Styling Friendly**

Every parameter is a plain serialized field with a dirtying property, so it works with animation, the Property Bindings package and the Styling package out of the box.

# Installation

This package is available on [OpenUPM](https://openupm.com/packages/com.timbojimbo.ui)

1. Add the Scoped Registry:
	- Open **Edit > Project Settings > Package Manager**
	- Add a new Scoped Registry (or append the missing scope if you already have one):
		- Name: `OpenUPM`
		- URL: `https://package.openupm.com/`
		- Scope(s): `com.timbojimbo`
2. Install the package
	- Open **Window > Package Manager**
	- Click Add and select **Add package by name...**
	- Paste name: `com.timbojimbo.ui`

Done!

> [!WARNING]
> This package is new - use at your own risk! :)

<details>
<summary>Install from GitHub instead (Not Recommended)</summary>

You can also add it directly from GitHub. Note that you won't be able to receive updates through Package Manager this way, you'll have to update manually.

- Open **Window > Package Manager**
- Click add and select **Add package from git URL...**
- Paste `https://github.com/Timbo-Jimbo/UI.git?path=com.timbojimbo.ui`
</details>

# Usage

## Box

Add **Timbo Jimbo > UI > Box** to a `RectTransform`. The component adds the extra canvas shader channels it needs automatically.

A rounded card with a shadow and a border is three objects with the same stretch anchors:

| Object | Colour | Corner Radius | Blur | Stroke | Offset |
|---|---|---|---|---|---|
| Shadow | black, 50% alpha | 24 | 24 | 0 | (0, -12) |
| Panel | white | 24 | 0 | 0 | (0, 0) |
| Border | accent | 24 | 0 | 2 | (0, 0) |

- **Corner Radius** is clamped to half the short side, so a short wide box becomes a pill and a square becomes a circle.
- **Corner Shape** is a row of preset buttons (Circle, Parabola, Cosine, Cubic) that set the **Curvature** slider. A curvature between presets highlights no button. Near the size limit the shader eases the curvature back to circular so pills stay clean.
- **Concentric** takes the corner radii and curvature from the nearest parent Box instead, matched per corner so the two rounded rects stay concentric across the gap between them. A child inset by `d` on a corner gets that corner's parent radius minus `d`; a child outset by `d` (larger than the parent) gets the parent radius plus `d`, so the rounded rects stay parallel whether the child is smaller or larger (never below 0). It resolves recursively, so a concentric box under another concentric box still matches the outermost plain box, and it tracks the parent live as the parent's radii, curvature, size or inset change. This assumes the child and parent share a canvas and axis alignment (the normal case). With uneven offsets a corner follows the side that deviates most from the parent, since our circular corners cannot follow CSS's elliptical ones.
- **Inset** offsets the drawn box within its `RectTransform`, uniformly or per side, in canvas units. Positive shrinks it inward; negative grows it outward past the `RectTransform`, so the box (and any mask taken from it) can spill beyond its layout rect. The corners stay concentric with the full rect, and only the generated mesh changes: the `RectTransform` and layout are untouched. Because a `Mask` stencils from the drawn mesh, an inset Box masks its children to the offset shape without a padded child object, which keeps the masked content an immediate child for layout groups. Note this also offsets the box's own visible fill; to change only the stencil while keeping the fill in place, turn off **Show Mask Graphic** and draw the fill with a sibling, or wait for the planned soft-mask path.
- **Stroke Width** draws a border ring instead of a fill. A positive width draws the ring inside the edge (CSS border semantics), so it can share the panel's rect; a negative width draws it outside the edge, spilling past the box (the quad grows to fit).
- **Blur Radius** softens the edge; the quad grows to make room.
- **Offset** moves the drawn box without moving the `RectTransform`.
- **Masking** makes the box a UGUI stencil mask for its children, and manages the `Mask` component for you (it appears below the Box, shown but not editable). **None** does not mask. **Draw and Mask** draws the box and clips its children to its shape. **Mask Only** clips the children without drawing the box, so its **Color**, **Gradient** and **Blur** are hidden and treated as white / off / 0, giving a crisp stencil. Combined with **Inset**, a Mask Only box clips its immediate children to an inset rounded shape without a padded wrapper object.

## Gradient

Enable **Gradient** on any box to fill it with a three-stop gradient instead of the solid colour.

- **Interpolation** is the colour space the stops blend through: RGB, HSV, OkLab or OkLCh, the same options as the Property Bindings package. OkLab gives the most even perceptual blend.
- **Angle** is in degrees; 0 runs left to right, 90 runs bottom to top.
- The stops are edited on the **preview bar**: **From** and **To** sit at the ends and, with **Use Via Stop** on, a **Via** chevron appears in the middle. Drag the Via chevron to move it, and click any chevron to open the colour picker. The bar previews the real interpolation over a checker, so a stop's alpha reads correctly. **Via Position** is also a numeric slider for precise placement.
- The box's **Color** multiplies the whole gradient as a tint, and CanvasGroup alpha fades every stop, so gradients respond to colour and fades the same way a solid box does.

Gradients compose with everything else: a rounded, stroked, blurred box can also be a gradient, and gradient boxes still batch into one draw call.

## Img

Add **Timbo Jimbo > UI > Img** instead of Image.

Set a **Texture** on it to show a plain texture the way RawImage does, without a separate component. Img wraps the texture in a generated full-rect sprite and renders that; it takes precedence over Source Image while set, and clearing it falls back to the sprite. Only one shows at a time. The texture must be a `Texture2D`, and the generated sprite is cleaned up automatically.

Img is an `Image`, so its inspector is the stock one plus **Blend Mode** and Box's **Gradient** section with two stops (no via stop: its vertex data has no room for a third). The gradient tints the sprite the way **Color** does, evaluated per pixel across the whole rect: a sliced, tiled, filled or preserved-aspect sprite shows the part of the gradient it covers. **Color** still multiplies the result and CanvasGroup alpha fades every stop.

Every Img with the same blend mode shares one material, so icons keep batching as they would with the default UI material.

### Tiled

Img's **Tiled** type draws the tile grid in the shader from a single quad rather than Image's one-quad-per-tile mesh, which is what makes the extra controls possible. The grid only places stamps; the sprite settings say what is stamped at each point:

- **Sprite › Size** is the width of each stamp in canvas units; the height follows the sprite's aspect. 0 uses the sprite's native size (its pixel width over pixels per unit). There is no Pixels Per Unit Multiplier in this type.
- **Sprite › Rotation** turns each stamp about its placement point, in degrees counter-clockwise, independent of the grid's rotation.
- **Grid › Rotation** turns the placement grid about the rect centre. The stamps keep their own rotation, so a turned grid with upright sprites is one setting, not two.
- **Grid › Spacing** is the distance between placement points per axis, in canvas units (up to 8× the sprite size). 0 on an axis means the sprite size, so stamps sit edge to edge; 50 with a 50 sprite in a 100×100 rect is a 2×2 grid. Spacing below the sprite size cuts each stamp off at its cell's edge; stamps never overlap.
- **Grid › Stagger** shifts each successive row along x (X) and each successive column along y (Y), as a fraction of the spacing. (0.5, 0) is a brick pattern.
- **Grid › Offset** shifts the grid along its own axes, in canvas units, wrapping every cell. Animate it to scroll the grid.

The grid starts at the rect's bottom-left like Image's, each pixel belongs to the nearest placement point (cells are rectangles, or the lattice's parallelograms when staggered), and blur flows across stamps and gaps as it would across real geometry. Stamps never bleed their atlas neighbours: every sample is kept half a texel inside the sprite's rect. All of this travels in vertex data (the rotations, spacing, offset and pan as 12-bit pairs relative to the sprite, the stagger and colour blend factor as 8-bit values), so tiled Imgs still share the one material. The sprite's border and Fill Center are not used in this type; use Sliced for a 9-slice frame. Alpha hit testing (`alphaHitTestMinimumThreshold`) still maps through Image's own tile size and does not know about these controls.

### Color Blend

**Color Blend** chooses how the colour (flat or gradient) combines with the sprite's own colour, and **Factor** fades between the untouched sprite (0) and the fully blended result (1). Alpha is always sprite alpha × colour alpha, so the colour's alpha stays an opacity control in every mode.

| Mode | Result |
|---|---|
| Multiply | sprite × colour (the stock Image behaviour) |
| Replace | the colour, keeping the sprite's alpha: a silhouette recolour |
| Add | sprite + colour |
| Screen | inverse multiply, always lightens |
| Darken / Lighten | per-channel min / max |
| Overlay | multiply in dark areas, screen in light areas |
| Difference | absolute difference |

### Blur

**Blur Radius** (canvas units) blurs the sprite in a single pass, and **Quality** caps the tap count (up to 3×3, 5×5 or 7×7). The tap count grows with the radius toward that cap, so a small blur is cheap; the taps spread across the radius and each samples the mip whose texel pitch matches the tap spacing, so their footprints tile into a smooth result and cost is bounded by the quality, not the radius. Simple sprites grow their quad by the radius so the blur spills past the sprite's edge; sliced, tiled, filled and mesh sprites blur within their own geometry.

Once the radius exceeds what the tap budget covers, the blur keeps widening but softens rather than getting more expensive. At that point it samples a coarse mip, whose bilinear reconstruction would show a faint block grid; the shader jitters every tap independently by a fraction of a mip texel (an isotropic hash, no texture needed), turning the kernel into a stochastic estimate of a smoother one. The grid dissolves into fine, directionless grain, and averaging across the taps keeps that grain low. The jitter scales with the radius, so small blurs are untouched. It is a per-component toggle, **Jitter**, on by default; the flag rides in vertex data, so turning it off (for the raw blocks) keeps the shared material and adds no draw call. A wide, perfectly smooth blur is the job of a multi-pass (dual-Kawase) approach that scales logarithmically; that is a planned future option, since it needs its own render target and would leave the shared-material batch. For UI glows and soft shadows of a few to a couple of dozen pixels the single pass is smooth.

Two things still limit it, and the inspector warns about both:

- **Mipmaps.** Without them every tap lands on level 0 and wide radii read as ghost copies. Enable Generate Mip Maps on the texture or atlas; Trilinear filtering gives the smoothest result.
- **Dark seams between mip blocks.** By default Unity averages transparent texels (whose colour is black) into the coarse mips, so a wide blur shows dark grid seams around the sprite and its edges. Enable **Replicate Border** on the texture (or **Alpha Is Transparency**, which dilates the edge colour) so those mips carry colour instead of black. A Sprite Atlas does not expose this and its page mips are lower quality, so a standalone texture blurs noticeably cleaner; prefer one for sprites you blur widely.
- **Coarse mips.** Once a wide blur samples mips around 16 source pixels per texel, block compression (DXT, ETC, ASTC) is crude at that resolution and streaks through the blur, and in an atlas a tap's bilinear footprint can reach past the sprite's padding into its neighbours. Use an uncompressed format for that atlas, raise its padding, raise the quality, or keep the radius moderate. Typical icon use, a sprite near its native size with a small radius, stays on fine mips and needs none of this.

The blur averages the sprite in the framebuffer's colour space, matching how the rest of the UI blends. Blurred and sharp images share the same material, so blurring never adds a draw call.

## Crisp Small Icons (Sprite Atlas Mip Bias)

Icons drawn much smaller than their source texture look crunchy without mipmaps and soft with them, because a Sprite Atlas can only generate box-filtered mips and exposes no mip bias or Kaiser filtering. The package applies a small negative mip bias to every Sprite Atlas texture automatically: at runtime as each atlas registers, and in the editor after atlas import and domain reload, so previews match. Nothing to add per atlas.

The default is `-0.5`, which suits typical icon minification (a 128 px icon at 32 px). To change it, set `SpriteAtlasMipBias.Bias` from code at startup, before any atlas loads.

For the bias to do anything the atlas needs mipmaps. Recommended settings for an icon atlas:

- **Generate Mip Maps** on, **Filter Mode** Trilinear.
- **Padding** of 8 or more (a 4 px gutter is a fraction of a pixel a few mip levels down and neighbouring sprites bleed in).
- **Tight Packing** and **Allow Rotation** off.
- Source textures imported **uncompressed**, so the atlas packs lossless pixels (Unity warns about this on import).

Very large minification (a 512 px icon at 32 px) is limited by icon detail rather than filtering; author a smaller variant for those cases.

## Scripting API

```csharp
var box = gameObject.AddComponent<Box>();
box.color = Color.white;
box.SetCornerRadius(24f);                 // or box.CornerRadii = new Vector4(tl, tr, br, bl);
box.CornerCurvature = CornerShape.Cosine.ToCurvature();
box.StrokeWidth = 2f;
box.BlurRadius = 0f;
box.Offset = Vector2.zero;

// Gradient fill
box.GradientEnabled = true;
box.GradientMode = ColorInterpolationMode.OkLab;   // from TimboJimbo.Core
box.GradientAngle = 90f;                            // bottom to top
box.GradientFrom = Color.magenta;
box.GradientVia = Color.white;                      // set GradientUseVia = false to skip
box.GradientTo = Color.cyan;
box.GradientViaPosition = 0.5f;

// Blend mode (each mode resolves to its own shared material)
box.BlendMode = UiBlendMode.Additive;

// Img has the Image API plus the same gradient and blend mode members
var img = gameObject.AddComponent<Img>();
img.sprite = icon;
img.GradientEnabled = true;
img.GradientFrom = Color.white;
img.GradientTo = Color.gray;
img.BlendMode = UiBlendMode.Screen;
img.ColorBlendMode = ColorBlendMode.Replace;   // how the colour meets the sprite
img.ColorBlendFactor = 1f;
img.BlurRadius = 6f;                           // canvas units
img.BlurQuality = BlurQuality.Medium;          // 5x5 taps

// Tiled: shader-drawn grid; the sprite and the grid are set up separately
img.type = Image.Type.Tiled;
img.TileSize = 32f;                            // stamp width in canvas units; 0 = native size
img.TileSpriteRotation = 45f;                  // each stamp about its placement point
img.TileGridRotation = 15f;                    // the placement grid about the rect centre
img.TileSpacing = new Vector2(40f, 36f);       // between placement points; 0 = sprite size
img.TileStagger = new Vector2(0.5f, 0f);       // brick pattern
img.TileOffset = new Vector2(10f, 0f);         // canvas units, wraps every cell; animate it to scroll
```

## Blend Modes

Set **Blend Mode** on the component (or `BlendMode` in code) to choose how a box or image composites with what is behind it:

- **Normal** — standard alpha over.
- **Additive** — adds to the background; glows and light.
- **Multiply** — always darkens.
- **Screen** — always lightens.

Every element of a given component and mode shares one material and batches together; switching to a different mode moves it to that mode's material, which is a separate draw call. When authoring a material by hand, the Box and Img material inspectors show the same Blend Mode dropdown.

# Layout

Layout for UGUI, modelled on [Clay](https://github.com/nicbarker/clay) and moved like SwiftUI. A layout pass says where every node goes; nodes get there on springs, from where they are drawn and at the velocity they have.

## Nodes

Add a `LayoutNode` to a RectTransform. A node whose parent is not a node is a root: it keeps the rect it is given and lays its children out inside it. The system owns every other node's RectTransform.

```csharp
var row = gameObject.AddComponent<LayoutNode>();
row.Direction = LayoutDirection.LeftToRight;
row.Width = Sizing.Grow();
row.Height = Sizing.Fit();
row.ChildGap = 12f;
row.Padding = Insets.All(16f);
row.ChildAlignY = AlignY.Center;
```

- **Sizing** per axis: `Fit` (its content, with an optional min and max), `Grow` (a share of what is left), `Fixed`, or `Percent` of its parent. `AspectRatio` sets the height from the width.
- **Wrap** breaks the children into lines, `ChildGap` apart, as CSS's flex-wrap and SwiftUI's lazy grids: `Wrap.Lines` as text wraps (tags), `Wrap.Grid(3)` in lines of three equal cells, `Wrap.Adaptive(120f)` in as many cells at least 120 long as fit. Lines and Adaptive wrap left to right; a Grid either way (a shelf of two rows that scrolls sideways).
- **Content**: a component on the node implementing `ILayoutMeasurable` is its content, measured at the width it gets and told the size it is going to. `Img` is one, and the UI Text package's `TextBlock`.
- **Floating** takes a node out of the flow and places it against its parent, its root, or any other node in its tree (`FloatingAttach.Element`), following that node as it moves and scrolls.
- **Display**: `Hidden` keeps a node's space, `None` takes it out of layout.
- **Offset**, **Scale** and **Opacity** move, scale and fade a node without taking space, for gestures.

## Safe area

As in SwiftUI, a root keeps its content clear of the notch, rounded corners and home bar (`Screen.safeArea`) on the edges its `SafeArea` names, all of them by default, adding as much as it covers to its padding; its own background still fills the screen. A node with `IgnoresSafeArea` reaches back out to the screen's edge where it lies against the safe area, its padding growing by as much, so what is inside stays clear:

```csharp
header.IgnoresSafeArea = Edges.Top | Edges.Left | Edges.Right; // its colour under the notch, its title below it
list.IgnoresSafeArea = Edges.Bottom;                            // rows scroll under the home bar, and rest above it
```

A node floating against the root sits inside the safe area. The Game view's safe area is the whole screen: the Simulator shows a phone's.

## Animated changes

A change made inside `MotionSystem.Animate` moves every node it gives somewhere new on springs ([Motion](#motion)); any other change goes there at once.

```csharp
var snappy = MotionAnimation.Default.Use(MotionAnimationPreset.Snappy);
MotionSystem.Animate(snappy, () =>
{
    panel.Width = Sizing.Fixed(480f);
    badge.Display = DisplayMode.Visible;
}).Finished += () => Debug.Log("landed");
```

- A node's `Animation` overrides the change's for the node and everything inside it, as SwiftUI's `.transaction`: a device that turns at once (None) around an app that springs (its own). Left on Inherit, a node moves on the nearest one above it, or else on the change's. `LayoutSystem.AnimationOf(node)` says which.
- Nodes turn from where they are when a change interrupts them. `Fling` throws a node with a velocity, `Catch` stops it where it is drawn, and `Velocity` reads how fast it is moving.

## Show and hide

A node shown or hidden inside `Animate` plays its `DisplayEffect`: fading, shrinking and sliding past an edge of its parent, together, the same both ways. A hidden node is drawn until it has gone, and the change finishes then. `ShownChanged` hands the shown value to effects of your own.

## Names

Nodes with the same `MatchName` (and `MatchId`, set in code and inherited from above) pair up inside `Animate`:

- One shown as another is hidden **takes over** from where that one is drawn and flies to its own place, the two cross-fading: a cell zooming into the page it opens.
- One shown or hidden next to one that **stays shown grows out of it** and shrinks back into it: a dropdown's list out of its button.

`MatchFit` says how a matched node fills the rect it moves through, as CSS's `object-fit`. `MatchWidth`, the default as on the web, keeps it at its own size and scales it, as a picture of itself, evenly to that rect's width; `Fill`, `Contain` and `Cover` scale it to the rect exactly, to fit inside it or to cover it; `Resize` changes the rect's size instead, its content laid out at its own size. `MatchClip` cuts both halves to that rect while they fly. The node taking over decides for the pair: its fit, its clip, and the animation both halves move on, so an `Animation` given to one end plays the way a pair is taken over to it.

Pairs and nodes moved to a new parent inside `Animate` fly above everything, out of every clip, until they land.

## Scrolling

Set a node's `Scroll` and it clips its children and scrolls them, with drags, flicks, rubber banding, the wheel and touch to stop, as a UIScrollView.

- `ScrollOffset`, `ScrollTo` and `ScrollIntoView` scroll from code, on a spring inside `Animate`. `Scrolled` reports the offset.
- `ScrollAnchor` End keeps a chat or a log at its end as it grows.
- `ScrollSnap` rests on whole pages or on its children, one per flick, as UIKit's paging and SwiftUI's view-aligned scrolling.
- Scroll indicators show while it scrolls and fade out after, as on iOS (`ShowsScrollIndicators`, `ScrollIndicatorColor`).
- Nested lists pass a drag on from the inner one to the outer, as UIKit chains them. An `ILayoutDraggable` (a sheet's height, a card's pull) takes part too: offered each move before the lists inside it, or, with `PassOnMidDrag` false, given whole drags when the lists have no room.

# Motion

Changes made on springs, as SwiftUI's `withAnimation`: what a change moves sets off from where it is drawn, at the velocity it has, and the change says when everything it moved has landed. Layout and variants move with it.

- **Changes:** `MotionSystem.Animate` makes the change its update makes, and moves what it changes on springs. A change carries an animation, its own or the default; what it moves can have its own instead, such as a layout node's `Animation`. A change made inside another's update joins that one.
- **Springs as SwiftUI gives them:** a `MotionAnimation` is a perceptual duration and a bounce, with a delay, and a curvature that bows a move across the screen out sideways. The presets are Smooth, Snappy, Bouncy, Arc and None. A duration of 0 (None) is no animation: what moves is there at once, after its delay. Springs are stepped exactly, in closed form, so they play the same at any frame rate, and turn from where they are, at the speed they have, when a change gives them somewhere new to go.
- **Knowing when it lands:** the returned `MotionTransition` raises `Finished` once everything the change moved has landed or been taken over. `Completed` says whether it got there without being interrupted, and `Skip` puts everything where it was going. It carries type names, and `interactive: false` lets the pointer through what it moves until it lands.
- **Your own values:** a value you draw yourself (a colour, a radius, up to four numbers) moves with the change being made through `MotionSystem.AnimateValue`, and the change waits for it. `MotionSystem.Current` is the change being made, while `Animate`'s update runs.
- **UI time:** springs step once a frame, just before canvases are drawn, on unscaled time, so UI moves in a pause menu. In edit mode nothing animates.

```csharp
var snappy = MotionAnimation.Default.Use(MotionAnimationPreset.Snappy);
MotionSystem.Animate(snappy, () =>
{
    panel.Width = Sizing.Fixed(480f);                                                // a layout node springs to its new size
    MotionSystem.AnimateValue(this, "tint", Tint, Color.red, snappy, v => Tint = v);  // and a value of your own with it
}).Finished += () => Debug.Log("landed");
```

What a change moves is worked out once its update has run, so part of an update can't be given an animation of its own: a change made inside another joins it, on the outer change's animation.

# Variants

Variants that live inside a prefab and switch in place: prefab variants, if one instance could be any of them.

🎛️ **Variant Set**

A component on a prefab's root. Its variants come in groups that are set independently: a toast's **Type** (Success, Warning, Error) and its **Size** (Compact). Each group is on **Inherit** (as new groups are), **Default**, the prefab as it is, or one of its variants. Where two groups set the same value, the later one wins.

🧩 **Anything Serialized**

A variant is a set of values on objects under the set: colours, sprites, text, numbers, references, whether an object is active, even a field inside a struct (a button's normal colour). Values are written through each component's own property (`m_Color` through `color`, `_cornerRadii` through `CornerRadii`), so graphics redraw and layouts update as they would from code.

⏺️ **Record by Editing**

Select a variant and press **Record**. Edits to anything under the set, in the inspector or the scene view, go into the variant instead of the prefab. Undo works. Right-click any property to add it to the selected variant, or take it out.

👁️ **Preview Without Saving**

The selected variants show in the scene and in prefab mode through AnimationMode, the way the Animation window previews a clip. Scenes and prefabs keep and save their default values, so switching variants never dirties a prefab or leaves overrides behind. An instance can start on a variant: select it on the instance, and it shows that way in the editor.

🌀 **Animated**

A switch animates wherever it's made from: code, a button, a state, a breakpoint. It's SwiftUI's `.animation(_:value:)`, on the group's **Animation**: Inherit for the default, None for at once, or a preset of its own, such as a quick, snappy spring for a hover. The layout a switch changes springs, and colours and other numbers move on the spring of the layout node they're drawn in. A layout node with an Animation of its own keeps it. Text, sprites and active states change at once. To hide something animatedly, set its node's Display rather than its active state. Any switch made inside `MotionSystem.Animate` animates too, and joins that change.

🖱️ **Interaction States**

**Variant States** selects a variant as the pointer hovers and presses, as it takes focus while focus shows (after keyboard or gamepad input, not a click: `FocusSystem.FocusVisible`), and while it's disabled. It works on any object with a raycast target, so custom hover effects and custom buttons work as well as UGUI controls. One state shows at a time, Disabled first, then Pressed, Focused and Hover, as UIKit's and Selectable's do. Pressed drops when a touch drags out. A Selectable on the same object that isn't interactable disables it, and its own transition is left alone (set it to None).

📐 **Breakpoints**

**Variant Breakpoints** selects a variant by the size its object is drawn at, like CSS container queries and Tailwind's breakpoints, mobile first: Default below every breakpoint. It measures width, height or aspect ratio; orientation is an aspect breakpoint at 1. The editor previews the variant for the size it's drawn at, without saving it.

🌳 **Inherited**

A group on **Inherit** shows what the nearest set above it with a group of the same name shows, like SwiftUI's environment or UIKit's dark mode passing down the hierarchy. Author a badge as a prefab of its own with a Type group, put it in a toast, and it shows the toast's Type: Error there, Error here. A name it has no variant of shows Default, and still passes down. Selecting Default or a variant overrides Inherit for that set and everything under it. The inspector says where an inherited variant comes from. A switch passes down inside the same change, so everything moves together. A group that Variant States or Variant Breakpoints drive selects for itself, so breakpoints on an app's root drive the parts inside it. Where a set and one inside it both set a value, the outer one wins, as an outer prefab's overrides do.

```csharp
toast.Set("Type", "Error");   // or toast.Set("Error"): the first group with a variant of that name
toast.Clear("Type");          // back to Inherit: Default, unless a set above has a Type
card.Toggle("Expanded");      // on, or back to Inherit when it's on already
toast.Changed += set => Debug.Log(set.Get("Type"));  // what it shows, inherited or its own
```

Limits:

- A variant sets values; it does not add, remove or move objects.
- Array elements (a list's items, an event's listeners) can't be recorded.
- Runtime writes go through reflection, cached per type and property. With aggressive managed code stripping, a property only a variant uses could be stripped; keep it with a `link.xml`.
- The preview shares AnimationMode with the Animation window and Timeline, and waits while either is previewing.

# Focus

Keyboard and gamepad focus over UGUI's Selectables. They still do the selecting, and the input module's Navigate, Submit and Cancel still drive them; `FocusSystem` adds the parts UGUI leaves out, once a frame, with nothing to set up:

- **Only what can be used takes focus:** shown, not on its way out, taking the pointer, and inside the topmost modal. Arrows never land on a hidden page or under a backdrop.
- **Moves by where things are drawn:** a Selectable left on Automatic navigation is pointed at its neighbours, what lies in line with it first, then the nearest. When several are equally near (up from a wide row into a row of tabs), focus goes back to the one its scope last focused, else the scope's Default Focus, else the first in reading order. Explicit and None navigation are left as authored, and a slider or scrollbar keeps its own axis for changing its value.
- **Comes back:** when what has focus is hidden, disabled or destroyed, focus goes back where it belongs. Navigating with nothing focused puts focus somewhere first.
- **Tab and Shift-Tab** go through reading order.
- **Scrolls into view:** what takes focus is scrolled into view in every scroll container it's in.
- **Shows only when it should:** `FocusSystem.FocusVisible` is on after keyboard or gamepad input and off after a click or a touch, as CSS's `:focus-visible`. Variant States' Focused follows it.
- **Wakes before it moves:** while focus doesn't show, the first direction (or Tab) only shows it, on what has it, brought into view. Nothing moves until the next press: what has focus may have changed unseen, with the pointer.

A **Focus Scope** groups part of the UI:

- **Section** (as a sidebar, a list or a tab bar): focus moves in and out of it freely, landing on whatever is nearest the way you pressed, as SwiftUI's `focusSection`. Its **Default Focus** is where focus goes when what had it inside goes.
- **Enter At**: where focus coming in from outside lands. **Nearest** (the default) is whatever is nearest the way you pressed, right for lists side by side. **Last Focused Or Default** is what it last focused, else its default: its **Default Focus**, else its first item in reading order (top left). That's for something focus comes into as a whole and back to where it left, as UIKit's preferred focus and `remembersLastFocusedIndexPath`: a page beside its sidebar, the sidebar, a tab bar. A modal always takes focus this way as it appears.
- **Modal** (as a sheet): nothing outside it can be focused while it's active. It takes focus as it appears and gives it back as it goes, as UIKit restores focus after a modal transition.
- **Takes Cancel**: Escape, a gamepad's B, or Android's Back raises its **Cancelled** event while focus is inside it, for closing a sheet or going back.
- **New Indicator**: it has a focus indicator of its own, drawn on what has focus inside it while focus shows. See below.

## Focus indicator

A scope with **New Indicator** makes its indicator from **Indicator Prefab**, a prefab whose root is a Layout Node, the first time it shows. It's made as the last child of **Indicator Parent** (the scope's own object when empty), so it draws over what's there, and it's attached to what has focus (Attach To Element; the prefab's points and offset stay as they are). It springs from one focused element to the next on its own Animation, and appears and goes with its own Display Effect. A move that finds nothing to go to nudges it that way, and it springs back.

Scopes inside it without New Indicator use its indicator. A scope with New Indicator but no prefab uses the prefab of the nearest one above it, so one prefab on a scope at the top of the screen styles them all. Where an indicator lives decides what clips it: one made inside a scroll container is cut at its edges as the items are.

When focus moves from one indicator's scope into another's, the first leaves where it is and the second appears where focus lands, rather than flying across the screen. Motion stays inside a group, and the indicator appearing anew shows where a group starts.

Elements can still style their own focus (Variant States' Focused) alongside it. The prefab's graphics shouldn't take the pointer.

The package depends on the Input System for this.

# AI Usage Disclosure

Parts of this package were written with the help of AI tools. Everything is reviewed and tested by a human before release.
