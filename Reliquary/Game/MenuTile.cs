using MoleMole;
using UnityEngine;
using UnityEngine.UI;

namespace Reliquary;

// A Reliquary tile in the Paimon menu. The game's own list adds it, so it looks and scrolls like the others, but the
// game's controller navigation is fixed when the menu opens and never reaches it. A controller opens Reliquary with Y.
internal sealed class MenuTile(Steps steps, Action<string> log)
{
    private const string PagePath = "/Canvas/Pages/PlayerProfilePage";
    private const string Name = "Reliquary";
    private float nextLook;
    private GameObject page;
    private Transform tile;

    public bool Open => page && page.activeInHierarchy;

    public void Update(float now, Sprite icon)
    {
        if (now < nextLook)
            return;
        nextLook = now + 0.2f;
        page = GameObject.Find(PagePath);
        if (Open)
            steps.Run("add the Paimon menu tile", () => Ensure(icon));
    }

    public bool Clicked()
    {
        if (!Open || !tile || !Input.GetMouseButtonDown(0))
            return false;
        Canvas canvas = tile.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint(tile.TryCast<RectTransform>(), new Vector2(Input.mousePosition.x, Input.mousePosition.y), camera);
    }

    // The screen point at the middle of the left edge of the game's Quit Game button.
    public Vector2? QuitEdge()
    {
        if (!Open)
            return null;
        foreach (Button button in page.GetComponentsInChildren<Button>())
            for (Transform node = button.transform; node && node != page.transform; node = node.parent)
                if (node.name.StartsWith("BtnLogout", StringComparison.Ordinal) && node.gameObject.activeInHierarchy && node.TryCast<RectTransform>() is { } rect)
                {
                    Canvas canvas = rect.GetComponentInParent<Canvas>();
                    Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    Rect box = rect.rect;
                    Vector3 left = rect.TransformPoint(new Vector3(box.xMin, box.center.y, 0f));
                    return RectTransformUtility.WorldToScreenPoint(camera, left);
                }
        return null;
    }

    // Leaves the menu the way its own Leave button does.
    public void Leave()
    {
        foreach (Button button in page.GetComponentsInChildren<Button>())
        {
            for (Transform node = button.transform; node && node != page.transform; node = node.parent)
                if (node.name.StartsWith("BtnExit", StringComparison.Ordinal) && button.gameObject.activeInHierarchy)
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
            return;
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
