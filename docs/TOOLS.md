# Tool Listing — 92 tools

> **Generated file — do not edit by hand.**
>
> Source of truth: `src/Fusion360.Mcp.Core/Catalog/tools_catalog.json`
> (written by `python3 scripts/generate_tool_catalog.py` from the
> reference `tools.py`). Regenerate this document with
> `python3 scripts/generate_tool_listing.py`; sync is enforced by
> `python3 scripts/check_guards.py` (drift -> exit 1).
>
> Groups mirror the “Available tools” section of `AGENTS.md`;
> the AGENTS.md table is a generated snapshot — do not edit it by
> hand; new tools are added via the catalog + regeneration.

| Tool | Group | Description | Read-only | Destructive | Idempotent |
|---|---|---|---|---|---|
| `ping` | Scene & Query | Health check — returns immediately without touching Fusion API | ✓ |  | ✓ |
| `get_scene_info` | Scene & Query | Get design name, bodies, sketches, features, camera info | ✓ |  | ✓ |
| `get_object_info` | Scene & Query | Get detailed info about a named body or sketch | ✓ |  | ✓ |
| `get_bounding_box` | Scene & Query | Axis-aligned bounding box for a body or component by name. Returns min, max, size, and center in cm (Fusion internal units). For components, unions bounding boxes of all contained bodies. Useful for measuring imported reference geometry. | ✓ |  | ✓ |
| `list_components` | Scene & Query | List all components in the design | ✓ |  | ✓ |
| `get_design_type` | Design Type | Check if the design is in parametric or direct mode. Use this to detect accidental mode switches. | ✓ |  | ✓ |
| `set_design_type` | Design Type | Switch design type between 'parametric' and 'direct'. Use 'parametric' to recover from accidental direct-mode switches (equivalent to Capture Design History in the UI). |  |  | ✓ |
| `create_sketch` | Sketching | Create a new sketch on xy/yz/xz plane, optionally offset |  |  |  |
| `draw_rectangle` | Sketching | Draw a rectangle in the most recent sketch |  |  |  |
| `draw_circle` | Sketching | Draw a circle in the most recent sketch |  |  |  |
| `draw_line` | Sketching | Draw a line in the most recent sketch |  |  |  |
| `draw_arc` | Sketching | Draw an arc in the most recent sketch (center + start point + sweep angle) |  |  |  |
| `draw_spline` | Sketching | Draw a spline in the most recent sketch. Use fit_points for a curve through points, or control_points for a control-polygon spline. |  |  |  |
| `create_polygon` | Sketching | Draw a regular polygon in the most recent sketch |  |  |  |
| `add_constraint` | Sketching | Add a geometric constraint in the active sketch. Entities are referenced by index within the sketch. |  |  |  |
| `auto_constrain` | Sketching | Automatically add geometric constraints and dimensions to fully constrain a sketch (Fusion 2026+ AutoConstrain API). result_option: 1 = thorough/slow (default), 2 = fast, 3 = may move geometry within tolerance. |  |  |  |
| `add_dimension` | Sketching | Add a driving dimension to constrain sketch geometry. Value is in cm for distances, degrees for angles. |  |  |  |
| `offset_curve` | Sketching | Offset connected sketch curves by a distance. Direction is determined by the direction_point. |  |  |  |
| `trim_curve` | Sketching | Trim a sketch curve at its intersections. The segment nearest to the given point is removed. |  |  |  |
| `extend_curve` | Sketching | Extend a sketch curve to the nearest intersection. The end nearest to the given point is extended. |  |  |  |
| `project_geometry` | Sketching | Project edges or bodies onto the active sketch plane |  |  |  |
| `extrude` | Features | Extrude a sketch profile |  |  |  |
| `revolve` | Features | Revolve a sketch profile around an axis |  |  |  |
| `sweep` | Features | Sweep a sketch profile along a path (sketch curve) |  |  |  |
| `loft` | Features | Loft between two or more sketch profiles |  |  |  |
| `fillet` | Features | Round edges of a body |  |  |  |
| `chamfer` | Features | Chamfer edges of a body |  |  |  |
| `shell` | Features | Hollow out a body by removing a face |  |  |  |
| `mirror` | Features | Mirror a body across a construction plane |  |  |  |
| `create_hole` | Features | Create a hole feature on a body face |  |  |  |
| `rectangular_pattern` | Features | Pattern a body in rows and columns |  |  |  |
| `circular_pattern` | Features | Pattern a body around an axis |  |  |  |
| `create_thread` | Features | Add threads to a cylindrical face (cosmetic or modeled) |  |  |  |
| `draft_faces` | Features | Add a draft angle to faces of a body (for mold release / injection molding) |  |  |  |
| `split_body` | Features | Split a body using a plane or face |  |  |  |
| `split_face` | Features | Split faces of a body using a plane |  |  |  |
| `offset_faces` | Features | Push/pull faces of a body by a distance |  |  |  |
| `scale_body` | Features | Scale a body uniformly or non-uniformly |  |  |  |
| `suppress_feature` | Features | Suppress (disable) a feature in the timeline |  |  |  |
| `unsuppress_feature` | Features | Unsuppress (re-enable) a feature in the timeline |  |  |  |
| `move_body` | Body Operations | Translate a named body by (x, y, z) |  |  |  |
| `rename_body` | Body Operations | Rename a body (searches root and all components) |  |  | ✓ |
| `boolean_operation` | Body Operations | Combine two named bodies (join/cut/intersect) |  |  |  |
| `delete_all` | Body Operations | Clear the design (delete all timeline items) |  | ✓ |  |
| `undo` | Body Operations | Undo the last operation |  |  |  |
| `create_box` | Direct Primitives | Create a box primitive (non-parametric via TemporaryBRepManager) |  |  |  |
| `create_box_parametric` | Direct Primitives | Create a history-based rectangular box via sketch rectangle + extrude (unlike create_box which uses TemporaryBRepManager). length/width/height accept a number (cm, Fusion internal unit) or a string expression referencing User Parameters (e.g. 'boxL', '56 mm', 'outer - 2 * wall_t'). Call create_parameter first to define named parameters. |  |  |  |
| `create_cylinder` | Direct Primitives | Create a cylinder primitive (non-parametric via TemporaryBRepManager) |  |  |  |
| `create_sphere` | Direct Primitives | Create a sphere primitive (non-parametric via TemporaryBRepManager) |  |  |  |
| `create_torus` | Direct Primitives | Create a torus primitive (non-parametric via TemporaryBRepManager) |  |  |  |
| `patch_surface` | Surface Operations | Create a patch surface from boundary edges |  |  |  |
| `stitch_surfaces` | Surface Operations | Stitch surface bodies into a single body |  |  |  |
| `thicken_surface` | Surface Operations | Thicken a surface body into a solid |  |  |  |
| `ruled_surface` | Surface Operations | Create a ruled surface from an edge or sketch curve |  |  |  |
| `trim_surface` | Surface Operations | Trim a surface body with another body |  |  |  |
| `create_flange` | Sheet Metal | Create a sheet metal flange on an edge |  |  |  |
| `create_bend` | Sheet Metal | Add a bend to a sheet metal body |  |  |  |
| `flat_pattern` | Sheet Metal | Create a flat pattern from a sheet metal body |  |  |  |
| `unfold` | Sheet Metal | Unfold specific bends in a sheet metal body |  |  |  |
| `create_construction_plane` | Construction Geometry | Create a construction plane for sketching |  |  |  |
| `create_construction_axis` | Construction Geometry | Create a construction axis |  |  |  |
| `create_ucs` | Construction Geometry | Create a UCS at (x, y, z) with optional rotation (Fusion 2026+ UCS API, preview). A hidden reference sketch is created to anchor the UCS — do not delete it. |  |  |  |
| `create_component` | Assembly | Create a new component (sub-assembly) in the design |  |  |  |
| `add_joint` | Assembly | Add a joint between two components |  |  |  |
| `create_as_built_joint` | Assembly | Create a joint from components' current positions (easier than geometric joints) |  |  |  |
| `create_rigid_group` | Assembly | Lock multiple components together |  |  |  |
| `measure_distance` | Inspection & Analysis | Measure minimum distance between two entities | ✓ |  | ✓ |
| `measure_angle` | Inspection & Analysis | Measure angle between two entities | ✓ |  | ✓ |
| `get_physical_properties` | Inspection & Analysis | Get mass, volume, surface area, center of mass, and density of a body | ✓ |  | ✓ |
| `create_section_analysis` | Inspection & Analysis | Cut a section plane through the model |  |  |  |
| `check_interference` | Inspection & Analysis | Detect collisions between components/bodies | ✓ |  | ✓ |
| `compare_meshes` | Inspection & Analysis | Compare two mesh bodies and return deviation statistics (min/max/mean/RMS signed distance in cm) — e.g. validate an imported STL against a reference mesh. Requires Fusion 2026+. | ✓ |  | ✓ |
| `set_appearance` | Appearance & Parameters | Assign a material appearance to a body, face, or component from the Fusion appearance library |  |  | ✓ |
| `set_color` | Appearance & Parameters | Assign a flat RGB color to a body (creates/reuses a design-local appearance). Useful for visually distinguishing parts before render_view. |  |  | ✓ |
| `get_parameters` | Appearance & Parameters | List all user parameters in the design | ✓ |  | ✓ |
| `create_parameter` | Appearance & Parameters | Create a new user parameter |  |  |  |
| `set_parameter` | Appearance & Parameters | Update the value of an existing user parameter |  |  | ✓ |
| `delete_parameter` | Appearance & Parameters | Remove a user parameter |  | ✓ |  |
| `import_mesh` | Import / Export | Import a mesh file (STL, OBJ, or 3MF) as a mesh body. Returns the mesh name and bounding box. Use for reference geometry (e.g. exported SketchUp model). |  |  |  |
| `export_stl` | Import / Export | Export a named body as an STL file |  |  |  |
| `export_step` | Import / Export | Export a body or component as a STEP file |  |  |  |
| `export_f3d` | Import / Export | Export the design as a native Fusion 360 archive (.f3d) |  |  |  |
| `export_view_sheet` | Import / Export | Render canonical orthographic + isometric views as PNGs and emit a self-contained HTML sheet suitable for sharing with a mechanical engineer. Opens in any browser; Print -> Save as PDF for a static artifact. Restores the viewport camera after rendering. |  |  |  |
| `export` | Import / Export | Unified export wrapper — dispatches to export_stl / export_step / export_f3d based on format or file extension. Format is auto-detected from file_path extension if not specified. STL and STEP require body_name; F3D exports the whole design. |  |  |  |
| `cam_create_setup` | CAM / Manufacturing | Create a manufacturing setup for a body. Defines the stock, coordinate system, and operation type (milling/turning/cutting). |  |  |  |
| `cam_create_operation` | CAM / Manufacturing | Add a machining operation to a setup. Strategy determines the toolpath type. |  |  |  |
| `cam_generate_toolpath` | CAM / Manufacturing | Generate toolpaths for a specific operation or all operations in a setup |  |  |  |
| `cam_post_process` | CAM / Manufacturing | Post-process toolpaths to generate NC code (G-code) |  |  |  |
| `cam_list_setups` | CAM / Manufacturing | List all manufacturing setups in the document | ✓ |  | ✓ |
| `cam_list_operations` | CAM / Manufacturing | List operations within a setup | ✓ |  | ✓ |
| `cam_get_operation_info` | CAM / Manufacturing | Get details about a specific operation (strategy, tool, parameters, toolpath status) | ✓ |  | ✓ |
| `render_view` | Perception | Capture the active viewport as a PNG so you can visually verify the model. Pass a canonical view (iso/front/top/etc.) to reposition the camera first, or 'current' to keep it as is. Returns base64-encoded image bytes alongside metadata; the MCP server delivers it as an image content block. | ✓ |  | ✓ |
