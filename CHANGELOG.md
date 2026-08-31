# Changelog

All notable changes to the Vizor Grasshopper plug-in are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.1.0] - 2026-08-30

### Added

- **Scene Interactable Object** (`2_Content`) — build interactable AR objects from meshes, each with its own name, colour and display rule, publishing a configurable message when the user interacts with it in AR.
- **Display Rule** (`2_Content`) — define how AR scene geometry is anchored, including attaching geometry to a named robot link.
- **Task Trigger** (`3_Task`) — dispatch a task on demand instead of stepping a sequence. Each message on `Task/activate` publishes exactly one task, addressed by task ID or task name; the activatable set is published once on `Task/listing` when the component starts, and a session is bracketed on `Task/signal` with `start_job_trig` / `end_job_trig`. The component holds no sequence state — the backend decides what runs next and when — so it does not track task completion.

  Task Trigger and **TaskController** are safe on the same canvas, but should not both be *running* against the same worker pool: `Task/listing` is a replacement rather than a merge, both components number their tasks from 0, and both publish to `WorkerPool/task`.
- **Listen Topic** (`4_Utilities`) — subscribe to any ROS topic and read the last message on the canvas. Takes a topic name (default `/Robot/status`) and an `Active` toggle, and outputs the raw payload plus the local time it arrived.

### Changed

- **TaskController** can hot-swap task content while a job is running, via a right-click *On Input Change* setting (*Reset*, default, or *Dynamic Substitute*), and brackets every run with `start_job` / `end_job` on `Task/signal`. Online/offline control moved to the right-click menu.
- **ModelTracker** merges its transform outputs into one cumulative `Transform` and adds a *Manual* / *Dynamic* input-reset mode; in *Dynamic* mode AR pose deltas accumulate until the input geometry actually changes.
- **Publish Topic** selects its message type from the right-click menu instead of an input port, and now publishes properly typed `Bool`, `Int32` and `Float32` messages rather than routing everything through the string publisher.
- **Robot Trajectory Object** is down from five inputs to three: the parameter space (*Cartesian (TCP)* / *Joint*) moves to the right-click menu, and the two data ports merge into one that re-labels itself to match. The space is never inferred from the connected data.
- **ConstructTask** accepts robotic tasks with no trajectory, for non-motion steps such as gripper open/close or tool change; **MakeTrajectory**, **MotionSimulation** and **RobotExecution** handle the trajectory-free case accordingly. Missing-safety-zone notices are downgraded to remarks, and the zone input nickname changes from `Z` to `Zone`.
- **SimulateDevice** replaces its four trigger booleans with buttons on the component, labelled for the device type. Only the first is shown by default; the rest are enabled from the right-click menu.
- **RobotExecution** drops its `Physical` toggle — real versus simulated hardware is selected at the ROS launch level; use **RobotSimulator** to preview a task without moving the robot.
- **RobotSimulator** restarts the simulation when any part of the task content changes, not only when the task id changes.
- **MakeWireframe** takes a single generic `Geometry` tree in place of its rival `Breps` list and optional point input, under one rule: a branch produces a wireframe. A flat list is one branch and therefore one wireframe; a tree of N branches gives N wireframes. Brep edges are now traced as one continuous line by walking the edge graph, instead of concatenating edges and drawing a connector between every unconnected pair — a box traces in 25 points with no stray segments, where the old encoding took 36 and drew eleven diagonals. Curved edges are divided to a new `Curve Resolution` input rather than sampled at start/midpoint/end, and straight edges cost two points instead of three.
- **Construct Scene Content** and **Display Rule** have their own icons.

### Fixed

- Fixed topic subscriptions being shared across the whole connection: each component now subscribes under its own rosbridge id, so one component unsubscribing no longer silently cuts off every other component listening to the same topic. Subscribe frames also omit the message type rather than sending `"type": null`, which rosbridge rejected when the declared type conflicted with the topic's real one.
- Fixed a WebSocket timeout that could leave stale duplicate connections behind.
- Fixed a native mesh memory leak in **ModelTracker**, and a missing ROS-to-Rhino unit conversion on incoming pose translations.
- Fixed **RobotSimulator** silently publishing empty joint trajectories, plus its `Step Interval` read order, degenerate single-waypoint meshes, and `Trajectory Width` not converting into the document's unit system.
- Fixed robot-anchored AR content being rebased by the base frame’s origin alone, with its orientation discarded, so a rotated robot base misplaced every anchored mesh, wireframe and interactable. Content is now rebased by the full rigid world→base transform. **MakeText** did not rebase at all, leaving robot-anchored labels offset from the geometry they annotate even for an axis-aligned base; **MakeTrajectory** published its visualisation mesh base-local on the joint branch but world on the cartesian branch. Both now match. An axis-aligned base behaves exactly as before.
- Fixed **MakeWireframe** not rebasing point input to the target anchor, indexing past the end of a short attribute list, and desynchronising per-branch attributes when a branch was skipped.

### Breaking changes

Component GUIDs are unchanged throughout; wires into removed ports are dropped when an older definition is opened.

- **`Publish Topic` lost its `Message Type` input port.** Re-select the message type from the right-click menu — it falls back to `std_msgs/String`.
- **`RobotExecution` lost its `Physical` input port.** The component now always commands execution.
- **`TaskController` lost its `GH Control` input port.** Online/offline is now a right-click setting, defaulting to online.
- **`SimulateDevice` lost its four trigger boolean inputs.** They are buttons on the component now, so any upstream wiring into them is gone.
- **`Robot Trajectory Object` lost its `ParamSpace` port and merged `TCP Frames` / `Joint Values` into one.** Everything except `Target Robot` needs re-wiring, and joint-space definitions revert to the cartesian default — re-select *Joint* from the right-click menu. A *list of robots* no longer iterates the component; use one component per robot.
- **`MakeWireframe`’s `Breps` and `Optional Point Input` ports are replaced by one `Geometry` tree.** A wire into the old `Breps` port lands on `Geometry` and keeps working, but a wire into the old point port lands on the new `Curve Resolution` port and must be moved to `Geometry`. Point-driven content also changes AR object name and is rebased when anchored to a robot.

### Known issues

- On the cartesian branch, **Robot Trajectory Object** reports its trajectory frames in world coordinates while the joint branch reports them base-local. The consumers — **MotionSimulation**’s target TCP and **DeconstructTask**’s frame output — expect world, so joint-mode simulation is misplaced for a robot whose base is off the world origin. The published AR visualisation is unaffected.

### Development

- Post-build deployment to the Grasshopper Libraries folder now runs for Release builds as well as Debug.

## [2.0.0] - 2026-07-16

### Added

- Example files and repository documentation, covering scene setup, multi-actor tasks and human-robot collaboration ([#10](https://github.com/cxiliu/VizorGH/pull/10)).

### Changed

- Rewrote the WebSocket layer against the latest websocket-sharp ([#8](https://github.com/cxiliu/VizorGH/pull/8)).

Releases before 2.1.0 predate this changelog; the entry above is reconstructed from the [GitHub release](https://github.com/cxiliu/VizorGH/releases/tag/v2.0.0).

[2.1.0]: https://github.com/cxiliu/VizorGH/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/cxiliu/VizorGH/releases/tag/v2.0.0
