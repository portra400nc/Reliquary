using Il2CppInterop.Runtime;
using MoleMole;
using UnityEngine;
using UnityEngine.UI;

namespace Reliquary;

// A Reliquary tile in the Paimon menu. The game's own list adds it, so it looks and scrolls like the others, but the
// game's controller navigation is fixed when the menu opens and never reaches it. A controller opens Reliquary with Y.
internal sealed class MenuTile(Steps steps, Action<string> log)
{
    private const string PagesPath = "/Canvas/Pages", PageName = "PlayerProfilePage";
    private const string Name = "Reliquary";
    private float nextLook, nextPagesLook;
    private Transform pages;
    private GameObject page;
    private static readonly string[] LeaveButtons = ["BtnExit", "BtnReturn"];
    private readonly Dictionary<string, Transform> leave = [];
    private Transform tile, quit;

    public bool Open => page && page.activeInHierarchy;

    // GameObject.Find searches the whole scene, about 5 ms on the Deck, and finds only active objects. So the pages'
    // parent is found once, and the menu page under it by name, which finds it while it is hidden too.
    public void Update(float now, Sprite icon)
    {
        if (now < nextLook)
            return;
        nextLook = now + 0.2f;
        if (!page)
        {
            if (!pages && now >= nextPagesLook)
            {
                nextPagesLook = now + 1f;
                pages = GameObject.Find(PagesPath)?.transform;
            }
            page = pages ? pages.Find(PageName)?.gameObject : null;
        }
        if (Open)
            steps.Run("add the Paimon menu tile", () => Ensure(icon));
    }

    // The first descendant whose name starts with prefix. A walk of the page instead of GetComponentsInChildren<Button>,
    // whose first call took 3.5 s on the Deck and froze the first opening of the menu.
    private static Transform Descendant(Transform parent, string prefix)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.StartsWith(prefix, StringComparison.Ordinal))
                return child;
            if (Descendant(child, prefix) is { } found)
                return found;
        }
        return null;
    }

    private static Button ButtonUnder(Transform node)
    {
        if (node.GetComponent(Il2CppType.Of<Button>()) is { } own)
            return own.TryCast<Button>();
        for (int i = 0; i < node.childCount; i++)
            if (ButtonUnder(node.GetChild(i)) is { } found)
                return found;
        return null;
    }

    public bool Clicked()
    {
        if (!Open || !tile || !Input.GetMouseButtonDown(0))
            return false;
        Canvas canvas = tile.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint(tile.TryCast<RectTransform>(), new Vector2(Input.mousePosition.x, Input.mousePosition.y), camera);
    }

    // The game's Quit Game button, found once by name.
    public RectTransform Quit
    {
        get
        {
            if (!Open)
                return null;
            if (!quit)
                quit = Descendant(page.transform, "BtnLogout");
            return quit ? quit.TryCast<RectTransform>() : null;
        }
    }

    // Leaves the menu the way its own Leave button does.
    // The controller layout's button is BtnExit_PS; with keyboard and mouse it is hidden and BtnReturn closes the menu.
    public void Leave()
    {
        foreach (string name in LeaveButtons)
        {
            if (page && (!leave.TryGetValue(name, out Transform found) || !found))
                leave[name] = Descendant(page.transform, name);
            if (leave.GetValueOrDefault(name) is { } node && node && node.gameObject.activeInHierarchy && ButtonUnder(node) is { } button)
            {
                button.onClick.Invoke();
                return;
            }
        }
        log("the Paimon menu has no Leave button to press");
    }

    private void Ensure(Sprite icon)
    {
        MonoSimpleReusableList list = page.GetComponent<MonoInLevelPlayerProfilePageV3>().monoEntryListTeyvat;
        Transform content = list.transform.Find("Content");
        tile = content.Find(Name);
        if (tile)
        {
            // The game's list hides the items it did not fill each time the menu opens.
            if (!tile.gameObject.activeSelf)
                tile.gameObject.SetActive(true);
            return;
        }
        tile = list.AddItemAddRefreshItemCount(content.GetChild(0).gameObject);
        tile.name = Name;
        MonoPlayerProfileMenuItem item = tile.GetComponent<MonoPlayerProfileMenuItem>();
        item.set_title(Name);
        if (icon != null)
            item.set_icon(icon);
        if (tile.Find("NoticeContainer") is { } notice)
            notice.gameObject.SetActive(false);
    }
}
