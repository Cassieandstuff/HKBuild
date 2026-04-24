using System.Text;
using System.Text.Json;

namespace HKBuild;

/// <summary>
/// Emits a self-contained HTML file with an interactive graph visualization
/// of a behavior using dagre-d3 (rendered via CDN).
/// </summary>
public class GraphEmitter
{
    private readonly BehaviorData _data;
    private readonly List<GraphNode> _nodes = [];
    private readonly List<GraphEdge> _edges = [];
    private readonly HashSet<string> _visited = [];

    public GraphEmitter(BehaviorData data) => _data = data;

    public string Emit()
    {
        // Walk from root generator.
        var root = _data.Behavior.Behavior.RootGenerator;
        WalkNode(root);

        var nodesJson = JsonSerializer.Serialize(_nodes);
        var edgesJson = JsonSerializer.Serialize(_edges);

        return BuildHtml(nodesJson, edgesJson);
    }

    private void WalkNode(string name)
    {
        if (string.IsNullOrEmpty(name) || name == "null" || !_visited.Add(name))
            return;

        var cls = _data.GetNodeClass(name);

        // State machines
        if (_data.StateMachines.TryGetValue(name, out var sm))
        {
            AddNode(name, "StateMachine", "#e74c3c");
            foreach (var stateName in sm.States)
            {
                AddEdge(name, stateName, "state");
                WalkNode(stateName);
            }
            if (sm.ParsedWildcardTransitions != null)
                foreach (var t in sm.ParsedWildcardTransitions)
                    if (t.ToState != null)
                        AddEdge(name, t.ToState, $"wc:{t.Event ?? "?"}", true);
            return;
        }

        // States
        if (_data.States.TryGetValue(name, out var state))
        {
            AddNode(name, "State", "#e67e22");
            if (!string.IsNullOrEmpty(state.Generator) && state.Generator != "null")
            {
                AddEdge(name, state.Generator, "gen");
                WalkNode(state.Generator);
            }
            if (state.ParsedTransitions != null)
                foreach (var t in state.ParsedTransitions)
                    if (t.ToState != null)
                        AddEdge(name, t.ToState, t.Event ?? "?", true);
            return;
        }

        // Clips
        if (_data.Clips.TryGetValue(name, out var clip))
        {
            var animShort = clip.AnimationName.Split('\\', '/').LastOrDefault() ?? clip.AnimationName;
            AddNode(name, $"Clip\\n{animShort}\\n{clip.Mode}", "#27ae60");
            return;
        }

        // Blender generators
        if (_data.Blenders.TryGetValue(name, out var blender))
        {
            var binding = blender.Bindings?.FirstOrDefault(b => b.MemberPath == "blendParameter");
            var label = binding != null ? $"Blender\\nvar:{binding.Variable}" : "Blender";
            AddNode(name, label, "#2980b9");
            foreach (var child in blender.Children)
            {
                AddEdge(name, child.Generator, $"w:{child.Weight}");
                WalkNode(child.Generator);
            }
            return;
        }

        // BSCyclicBlendTransitionGenerator
        if (_data.CyclicBlendGenerators.TryGetValue(name, out var cyclic))
        {
            var binding = cyclic.Bindings?.FirstOrDefault(b => b.MemberPath == "fBlendParameter");
            var label = binding != null ? $"Cyclic\\nvar:{binding.Variable}" : "Cyclic";
            AddNode(name, label, "#8e44ad");
            AddEdge(name, cyclic.PBlenderGenerator, "wraps");
            WalkNode(cyclic.PBlenderGenerator);
            return;
        }

        // Manual selector
        if (_data.Selectors.TryGetValue(name, out var selector))
        {
            var binding = selector.Bindings?.FirstOrDefault(b => b.MemberPath == "selectedGeneratorIndex");
            var label = binding != null ? $"Selector\\nvar:{binding.Variable}" : "Selector";
            AddNode(name, label, "#16a085");
            for (int i = 0; i < selector.Generators.Count; i++)
            {
                AddEdge(name, selector.Generators[i], $"[{i}]");
                WalkNode(selector.Generators[i]);
            }
            return;
        }

        // BSOffsetAnimationGenerator
        if (_data.OffsetAnimGenerators.TryGetValue(name, out var offset))
        {
            AddNode(name, "OffsetAnim", "#d35400");
            AddEdge(name, offset.PDefaultGenerator, "default");
            WalkNode(offset.PDefaultGenerator);
            AddEdge(name, offset.POffsetClipGenerator, "offset");
            WalkNode(offset.POffsetClipGenerator);
            return;
        }

        // Modifier generator
        if (_data.ModifierGenerators.TryGetValue(name, out var modGen))
        {
            AddNode(name, "ModifierGen", "#7f8c8d");
            AddEdge(name, modGen.Generator, "gen");
            WalkNode(modGen.Generator);
            AddEdge(name, modGen.Modifier, "mod");
            WalkNode(modGen.Modifier);
            return;
        }

        // Behavior references
        if (_data.BehaviorReferences.TryGetValue(name, out var bRef))
        {
            AddNode(name, $"BehaviorRef\\n{bRef.BehaviorName}", "#95a5a6");
            return;
        }

        // BSBoneSwitchGenerator
        if (_data.BoneSwitchGenerators.TryGetValue(name, out var boneSwitch))
        {
            AddNode(name, "BoneSwitch", "#c0392b");
            AddEdge(name, boneSwitch.PDefaultGenerator, "default");
            WalkNode(boneSwitch.PDefaultGenerator);
            if (boneSwitch.Children != null)
                foreach (var child in boneSwitch.Children)
                {
                    AddEdge(name, child.PGenerator, "case");
                    WalkNode(child.PGenerator);
                }
            return;
        }

        // BSiStateTaggingGenerator
        if (_data.StateTaggingGenerators.TryGetValue(name, out var stg))
        {
            AddNode(name, $"StateTag\\niState:{stg.IStateToSetAs}", "#2c3e50");
            AddEdge(name, stg.PDefaultGenerator, "gen");
            WalkNode(stg.PDefaultGenerator);
            return;
        }

        // PoseMatchingGenerator
        if (_data.PoseMatchingGenerators.TryGetValue(name, out var pm))
        {
            AddNode(name, "PoseMatch", "#1abc9c");
            if (pm.Children != null)
                foreach (var child in pm.Children)
                {
                    AddEdge(name, child.Generator, $"w:{child.Weight}");
                    WalkNode(child.Generator);
                }
            return;
        }

        // BSSynchronizedClipGenerator
        if (_data.SynchronizedClips.TryGetValue(name, out var sync))
        {
            AddNode(name, "SyncClip", "#f39c12");
            AddEdge(name, sync.PClipGenerator, "clip");
            WalkNode(sync.PClipGenerator);
            return;
        }

        // Modifier lists
        if (_data.ModifierLists.TryGetValue(name, out var modList))
        {
            AddNode(name, "ModList", "#bdc3c7");
            foreach (var m in modList.Modifiers)
            {
                AddEdge(name, m, "mod");
                WalkNode(m);
            }
            return;
        }

        // BSIsActiveModifier
        if (_data.IsActiveModifiers.TryGetValue(name, out var isActive))
        {
            AddNode(name, "IsActiveMod", "#bdc3c7");
            return;
        }

        // EventDrivenModifier
        if (_data.EventDrivenModifiers.TryGetValue(name, out var edm))
        {
            AddNode(name, "EvtDrivenMod", "#bdc3c7");
            AddEdge(name, edm.Modifier, "mod");
            WalkNode(edm.Modifier);
            return;
        }

        // Generic modifiers
        if (_data.GenericModifiers.TryGetValue(name, out var gm))
        {
            AddNode(name, $"Mod:{gm.Class.Replace("hkb", "").Replace("Modifier", "")}", "#bdc3c7");
            return;
        }

        // Fallback
        AddNode(name, cls ?? "Unknown", "#ecf0f1");
    }

    private void AddNode(string id, string typeLabel, string color)
    {
        _nodes.Add(new GraphNode { id = id, label = $"{id}\\n({typeLabel})", color = color });
    }

    private void AddEdge(string from, string to, string label, bool isDashed = false)
    {
        if (string.IsNullOrEmpty(to) || to == "null") return;
        _edges.Add(new GraphEdge { from = from, to = to, label = label, dashed = isDashed });
    }

    private static string BuildHtml(string nodesJson, string edgesJson)
    {
        return $$"""
        <!DOCTYPE html>
        <html><head>
        <meta charset="utf-8">
        <title>HKBuild — Behavior Graph</title>
        <script src="https://d3js.org/d3.v7.min.js"></script>
        <script src="https://cdn.jsdelivr.net/npm/dagre-d3@0.6.4/dist/dagre-d3.min.js"></script>
        <style>
            body { margin: 0; background: #1a1a2e; overflow: hidden; font-family: monospace; }
            svg { width: 100vw; height: 100vh; }
            .node rect { stroke: #333; stroke-width: 1.5px; rx: 5; ry: 5; }
            .node text { fill: #fff; font-size: 11px; }
            .edgePath path { stroke: #999; fill: none; stroke-width: 1.5px; }
            .edgePath .dashed { stroke-dasharray: 5,5; stroke: #e67e22; }
            .edgeLabel text { fill: #ccc; font-size: 9px; }
            #controls { position: fixed; top: 10px; left: 10px; color: #ccc; font-size: 12px; z-index: 10; }
            #controls button { background: #333; color: #ccc; border: 1px solid #555; padding: 4px 10px;
                               cursor: pointer; margin-right: 5px; border-radius: 3px; }
            #controls button:hover { background: #555; }
        </style>
        </head><body>
        <div id="controls">
            <button onclick="resetZoom()">Reset</button>
            <button onclick="toggleDirection()">Toggle LR/TB</button>
            <span id="info">Scroll to zoom, drag to pan</span>
        </div>
        <svg id="svg"><g id="inner"></g></svg>
        <script>
        const nodes = {{nodesJson}};
        const edges = {{edgesJson}};
        let rankdir = "TB";

        function render() {
            const g = new dagreD3.graphlib.Graph({ compound: true })
                .setGraph({ rankdir, ranksep: 50, nodesep: 30, edgesep: 15, marginx: 20, marginy: 20 })
                .setDefaultEdgeLabel(() => ({}));

            nodes.forEach(n => {
                g.setNode(n.id, {
                    label: n.label.replace(/\\n/g, "\n"),
                    style: "fill:" + n.color,
                    shape: "rect",
                    padding: 8
                });
            });

            edges.forEach(e => {
                g.setEdge(e.from, e.to, {
                    label: e.label || "",
                    curve: d3.curveBasis,
                    class: e.dashed ? "dashed" : ""
                });
            });

            const svgEl = d3.select("#svg");
            const inner = svgEl.select("#inner");
            inner.selectAll("*").remove();

            const renderer = new dagreD3.render();
            renderer(inner, g);

            const zoom = d3.zoom().on("zoom", (evt) => inner.attr("transform", evt.transform));
            svgEl.call(zoom);

            // Initial fit
            const gWidth = g.graph().width || 100;
            const gHeight = g.graph().height || 100;
            const svgWidth = svgEl.node().getBoundingClientRect().width;
            const svgHeight = svgEl.node().getBoundingClientRect().height;
            const scale = Math.min(svgWidth / (gWidth + 40), svgHeight / (gHeight + 40), 1.5);
            const tx = (svgWidth - gWidth * scale) / 2;
            const ty = (svgHeight - gHeight * scale) / 2;
            svgEl.call(zoom.transform, d3.zoomIdentity.translate(tx, ty).scale(scale));

            window._zoom = zoom;
            window._svg = svgEl;
        }

        function resetZoom() { window._svg.transition().duration(300).call(window._zoom.transform, d3.zoomIdentity); }
        function toggleDirection() { rankdir = rankdir === "TB" ? "LR" : "TB"; render(); }

        render();
        </script>
        </body></html>
        """;
    }

    private record GraphNode
    {
        public string id { get; init; } = "";
        public string label { get; init; } = "";
        public string color { get; init; } = "";
    }

    private record GraphEdge
    {
        public string from { get; init; } = "";
        public string to { get; init; } = "";
        public string label { get; init; } = "";
        public bool dashed { get; init; }
    }
}
