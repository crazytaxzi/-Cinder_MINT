# MintyFilter MintyBay Interaction Specification

Branch lineage: `MintyFilter` -> `MintyFilter-patchbay-controls`.

MintyBay is an editor first. Selecting, moving, wiring, panning, and inspecting are intentionally separate actions so ordinary mouse gestures never open controls by surprise.

## View navigation

| Gesture | Action |
| --- | --- |
| Mouse wheel | Pan vertically |
| Alt + mouse wheel | Pan horizontally |
| Ctrl + mouse wheel | Zoom around the mouse cursor |
| Right-button drag | Pan freely in both axes |
| Escape | Cancel an in-progress cable, marquee, node drag, or pan |

Zoom is bounded so the graph cannot disappear into numerical dust or become absurdly large. Cursor-centered zoom keeps the point under the mouse stable while the view changes.

## Nodes and cables

- Left-drag a node body to move it.
- If several nodes are selected, dragging any selected node moves the whole group.
- Left-drag from a socket to create a cable.
- Starting a cable does not select the node and never opens its inspector.
- Clicking a node selects it but does not open its inspector.
- Double-click retains the fast bypass/enable behavior for processors.
- Node controls are opened explicitly from the context menu or the configurable Open Controls shortcut.
- Hovered cables are highlighted to make precise removal easier.

## Marquee selection

- Left-drag empty canvas to draw a selection rectangle.
- Nodes intersecting the marquee become selected.
- Ctrl or Shift while starting the marquee adds to the existing selection.
- Ctrl or Shift clicking an already selected node removes it from the selection.
- Moving any selected node moves the whole selected set.

## Context menu

A right click without dragging opens a cursor-local menu. Right-drag is reserved for panning and suppresses the menu.

On empty canvas the menu offers node insertion at the exact graph cursor position.

On a node it offers:

- Open Node Controls
- Bypass / Enable
- Remove Node
- Add Node Here
- Edit Patchbay Shortcuts

On a cable it offers Remove Cable and Add Node Here.

On a socket it offers Disconnect plus the normal node insertion menu.

## Default editable shortcuts

| Action | Default |
| --- | --- |
| Add node at cursor | `N` |
| Remove hovered node or cable | `R` |
| Open controls for hovered/selected node | `Enter` |
| Toggle bypass for hovered/selected node | `B` |

The context menu exposes `Edit MintyBay Shortcuts...`. Shortcuts are persisted separately in `%APPDATA%\Cinder MINT\patchbay-hotkeys.json` so editing patch gestures does not dirty the audio graph or require an engine restart.

Escape is reserved as a universal cancel action and cannot be assigned.

## Destructive-action safety

Fast removal is still deliberate.

The `R` shortcut identifies the node or cable currently under the mouse and opens a confirmation window. The safe choice is the default:

- Enter -> Keep
- Escape -> Keep
- explicit click on Remove -> destructive action

Node deletion also removes cables attached to that node, and the confirmation states that explicitly.

Right-clicking a socket no longer destroys connections immediately. It opens a context action first.

## Interaction rule

MintyBay follows this priority:

1. socket drag = cable operation;
2. node drag = node/group movement;
3. empty-canvas left drag = marquee;
4. right drag = canvas pan;
5. simple click = selection only;
6. explicit menu/hotkey = inspector or destructive action.

No lower-priority behavior should leak through a higher-priority gesture.

