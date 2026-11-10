using UnityEngine;
using UnityEngine.UI;

namespace Reliquary.Ui;

// Builds the panel's parts from the style table, the shapes and the game's font, as CameraTools builds its own. The
// canvas holds only plain graphics: Genshin cannot run custom MonoBehaviours, so the mod writes every value to them.
internal sealed partial class Widgets(Font font)
{
    // A node under parent: in a redraw, the previous drawing's node of this name (Widgets.Reuse.cs), else a new one.
    public static RectTransform Node(string name, Transform parent)
    {
        if (Reusing && Reuse(name, parent) is { } reused)
            return reused;
        var rect = new GameObject(name).AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        if (Reusing)
            Track(rect, name, parent);
        return rect;
    }

    public static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    // Anchored and pivoted at the same point of the parent, such as (1, 0) for its bottom right corner.
    public static void Pin(RectTransform rect, float anchorX, float anchorY, float x, float y, float width, float height)
        => Place(rect, V(anchorX, anchorY), V(anchorX, anchorY), V(anchorX, anchorY), V(x, y), V(width, height));

    public static void TopLeft(RectTransform rect, float x, float y, float width, float height) => Pin(rect, 0f, 1f, x, -y, width, height);

    public static void Fill(RectTransform rect) => Place(rect, V(0f, 0f), V(1f, 1f), V(0.5f, 0.5f), V(0f, 0f), V(0f, 0f));

    // Unity's struct constructors are IL2CPP calls; filling the fields is not.
    public static Vector2 V(float x, float y) => new() { x = x, y = y };

    // A shape, or a plain rectangle without one. Nothing on the canvas takes the pointer.
    public static Image Picture(Transform parent, string name, Shape shape, Color color)
    {
        var image = Get<Image>(Node(name, parent).gameObject);
        if (shape != null)
        {
            image.sprite = Shapes.Get(shape);
            image.type = shape.Border > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.rectTransform.sizeDelta = V(shape.Width, shape.Height);
        }
        else
        {
            // A reused image may carry the last drawing's shape.
            image.sprite = null;
            image.type = Image.Type.Simple;
        }
        image.preserveAspect = false;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor alignment)
    {
        var label = Get<Text>(Node(name, parent).gameObject);
        label.font = font;
        label.fontSize = size;
        // The game's font is already heavy, and Unity fakes bold on a dynamic font by smearing it.
        label.fontStyle = FontStyle.Normal;
        label.color = color;
        label.alignment = alignment;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.supportRichText = false;
        label.resizeTextForBestFit = false;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    // CSS's text-shadow blurs; uGUI's Shadow is a sharp copy one unit down.
    public static void DropShadow(Graphic graphic)
    {
        var shadow = Get<Shadow>(graphic.gameObject);
        shadow.effectColor = Style.TextShadow;
        shadow.effectDistance = V(0f, -1f);
    }

    // Lays children out left to right at their preferred widths, leaving their heights alone.
    public static HorizontalLayoutGroup Flow(GameObject target, float spacing, TextAnchor alignment)
    {
        var group = Get<HorizontalLayoutGroup>(target);
        group.spacing = spacing;
        group.childAlignment = alignment;
        group.childControlWidth = true;
        group.childControlHeight = false;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        return group;
    }

    // A controller face button: a dark disc with its letter in the button's colour, or the D-pad.
    public void Glyph(Transform parent, string letter, Color ink)
    {
        Shape shape = letter == null ? Shapes.Dpad : Shapes.Face;
        var rect = Picture(parent, letter ?? "Dpad", shape, Style.White).rectTransform;
        var element = Get<LayoutElement>(rect.gameObject);
        element.preferredWidth = shape.Width;
        element.preferredHeight = shape.Height;
        if (letter != null)
            Fill(Label(rect, "Letter", letter, Style.FaceSize, ink, TextAnchor.MiddleCenter).rectTransform);
    }

    // A picture as wide as its text plus padding, at least minWidth: a keycap, a bumper or a tab chip.
    public (Image Plate, Text Label) Cap(Transform parent, string name, Shape shape, float height, float minWidth, int padding, int size, Color ink)
    {
        var image = Picture(parent, name, shape, Style.White);
        image.rectTransform.sizeDelta = V(minWidth, height);
        var group = Flow(image.gameObject, 0f, TextAnchor.MiddleCenter);
        group.padding.left = padding;
        group.padding.right = padding;
        Get<LayoutElement>(image.gameObject).minWidth = minWidth;
        var label = Label(image.rectTransform, "Text", name, size, ink, TextAnchor.MiddleCenter);
        label.rectTransform.sizeDelta = V(0f, height);
        return (image, label);
    }

    public void Keycap(Transform parent, string name)
        => Cap(parent, name, Shapes.Key, Style.KeyHeight, Style.KeyMinWidth, Style.KeyPadding, Style.KeySize, Style.Text);

    public void Bumper(Transform parent, string name)
        => Cap(parent, name, Shapes.Bumper, Style.BumperHeight, Style.BumperMinWidth, Style.BumperPadding, Style.BumperSize, Style.BumperInk);

    // A glyph or keycap, then its label.
    public void Hint(Transform parent, Action<Transform> mark, string label)
    {
        var rect = Node("Hint", parent);
        rect.sizeDelta = V(0f, Style.HintHeight);
        Flow(rect.gameObject, Style.HintGap, TextAnchor.MiddleLeft);
        mark(rect);
        var text = Label(rect, "Label", label, Style.HintSize, Style.Text, TextAnchor.MiddleLeft);
        text.rectTransform.sizeDelta = V(0f, Style.HintHeight);
        DropShadow(text);
    }

    // A row's selection band and arrow, under the row's own parts.
    public (GameObject Band, GameObject Arrow) Selection(RectTransform row)
    {
        var band = Picture(row, "Band", null, Style.Band);
        Fill(band.rectTransform);
        var arrow = Picture(row, "Arrow", Shapes.Arrow, Style.Text);
        TopLeft(arrow.rectTransform, Style.ArrowX, Style.ArrowY, Shapes.Arrow.Width, Shapes.Arrow.Height);
        band.gameObject.SetActive(false);
        arrow.gameObject.SetActive(false);
        return (band.gameObject, arrow.gameObject);
    }
}
