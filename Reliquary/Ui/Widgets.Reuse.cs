using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Reliquary.Ui;

// The panel redraws everything on every change, but creating Unity objects is most of a redraw's cost on the Deck
// (13 of 17 ms for 216 nodes, measured 2026-11-10), and new texts rebuild their meshes on the next frame. So a redraw
// reuses the nodes of the one before: within a session, Node takes the previous drawing's node of the same name under the
// same parent, in drawing order, and the session ends by destroying the nodes it did not take. Unchanged values then
// cost Unity nothing. The record lives here, not in the hierarchy, so ghosts and other nodes added outside a session
// are never taken.
internal sealed partial class Widgets
{
    private sealed class Built(RectTransform rect, string name, Built parent)
    {
        public readonly RectTransform Rect = rect;
        public readonly string Name = name;
        public Built Parent = parent;
        // Children drawn in the last session that reached this node, and in this one.
        public List<Built> Old = [], New = [];
        public int Seen, Next;
        // Set once the drawing order no longer matches the node's children: every later child then moves to the end.
        public bool Moved;
    }

    private static readonly Dictionary<IntPtr, Built> built = [];
    private static int session;
    private static Built sessionRoot;

    public static bool Reusing => sessionRoot != null;

    // Starts a redraw of root's subtree. Root itself stays; its children are drawn again with Node.
    public static void BeginReuse(RectTransform root)
    {
        session++;
        if (!built.TryGetValue(root.Pointer, out Built node) || !node.Rect)
            built[root.Pointer] = node = new Built(root, root.name, null);
        Take(node);
        sessionRoot = node;
    }

    // Destroys what the redraw did not draw again.
    public static void EndReuse()
    {
        Built root = sessionRoot;
        sessionRoot = null;
        if (root != null)
            Sweep(root);
    }

    // Moves a node drawn in this session to the end of its parent, as SetAsLastSibling does for a new one.
    public static void ToEnd(RectTransform rect)
    {
        rect.SetAsLastSibling();
        if (sessionRoot == null || !built.TryGetValue(rect.Pointer, out Built node) || node.Parent is not { } owner)
            return;
        owner.Moved = true;
        // The record keeps the hierarchy's order, which the next session relies on.
        if (owner.New.Remove(node))
            owner.New.Add(node);
    }

    // A node that leaves the drawing on its own, as a ghost does, is never taken again.
    public static void Release(RectTransform rect)
    {
        if (!built.TryGetValue(rect.Pointer, out Built node))
            return;
        node.Parent?.New.Remove(node);
        node.Parent?.Old.Remove(node);
        Forget(node);
    }

    // After the whole canvas goes.
    public static void ForgetAll()
    {
        built.Clear();
        sessionRoot = null;
    }

    // The component on a node, added if the node is new or was something else before.
    public static T Get<T>(GameObject target) where T : Component
    {
        T found = target.GetComponent<T>();
        if (found)
            return found;
        // A node can hold one graphic: one that was an image before and is text now loses the image.
        if (typeof(Graphic).IsAssignableFrom(typeof(T)) && target.GetComponent<Graphic>() is { } other && other)
            Object.DestroyImmediate(other);
        return target.AddComponent<T>();
    }

    private static RectTransform Reuse(string name, Transform parent)
    {
        if (!built.TryGetValue(parent.Pointer, out Built owner) || owner.Seen != session)
            return null;
        List<Built> old = owner.Old;
        for (int i = owner.Next; i < old.Count; i++)
        {
            Built node = old[i];
            if (node.Seen == session || node.Name != name || !node.Rect)
                continue;
            // Old nodes skipped here are not taken later, so the nodes taken keep their order.
            owner.Next = i + 1;
            Take(node);
            owner.New.Add(node);
            GameObject target = node.Rect.gameObject;
            if (!target.activeSelf)
                target.SetActive(true);
            if (owner.Moved)
                node.Rect.SetAsLastSibling();
            return node.Rect;
        }
        return null;
    }

    private static void Track(RectTransform rect, string name, Transform parent)
    {
        if (!built.TryGetValue(parent.Pointer, out Built owner) || owner.Seen != session)
            return;
        var node = new Built(rect, name, owner) { Seen = session };
        built[rect.Pointer] = node;
        owner.New.Add(node);
        // A new node goes to the end, after old children that may still be taken.
        for (int i = owner.Next; i < owner.Old.Count; i++)
            if (owner.Old[i].Seen != session)
            {
                owner.Moved = true;
                break;
            }
    }

    private static void Take(Built node)
    {
        node.Seen = session;
        node.Old = node.New;
        node.New = [];
        node.Next = 0;
        node.Moved = false;
    }

    private static void Sweep(Built node)
    {
        foreach (Built child in node.Old)
        {
            if (child.Seen == session)
                Sweep(child);
            else
            {
                if (child.Rect)
                    Object.Destroy(child.Rect.gameObject);
                Forget(child);
            }
        }
        node.Old = [];
    }

    private static void Forget(Built node)
    {
        built.Remove(node.Rect.Pointer);
        foreach (Built child in node.New)
            Forget(child);
        foreach (Built child in node.Old)
            Forget(child);
    }
}
