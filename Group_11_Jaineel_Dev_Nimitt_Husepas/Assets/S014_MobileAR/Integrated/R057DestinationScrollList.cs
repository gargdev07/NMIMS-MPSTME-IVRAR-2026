using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace R057.Kiosk
{
    public sealed class R057DestinationScrollList : MonoBehaviour
    {
        private S014.IntegratedDemo.S014MasterDemoController controller;
        private R057DestinationCatalog catalog;
        private Canvas canvas;
        private ScrollRect scrollRect;
        private RectTransform content;
        private string search = string.Empty;
        private string category = "All";
        private readonly List<GameObject> rows = new List<GameObject>();

        public void Initialize(S014.IntegratedDemo.S014MasterDemoController owner, R057DestinationCatalog source)
        {
            controller = owner;
            catalog = source;
            CreateView();
            Refresh();
        }

        public void SetVisible(bool visible)
        {
            if (canvas != null) canvas.enabled = visible;
        }

        public void SetSearch(string value)
        {
            if (search == value) return;
            search = value;
            Refresh();
        }

        public void SetCategory(string value)
        {
            if (category == value) return;
            category = value;
            Refresh();
        }

        public void Refresh()
        {
            if (content == null || catalog == null) return;
            foreach (var row in rows) if (row != null) Destroy(row);
            rows.Clear();

            foreach (var definition in catalog.Destinations)
            {
                if (category != "All" && definition.category != category) continue;
                if (!string.IsNullOrWhiteSpace(search) &&
                    definition.displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                AddRow(definition);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scrollRect.verticalNormalizedPosition = 1f;
        }

        private void CreateView()
        {
            var canvasObject = new GameObject("DestinationScrollCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var kioskObject = new GameObject("SmartCampusKiosk", typeof(RectTransform));
            kioskObject.transform.SetParent(canvas.transform, false);
            var kiosk = kioskObject.GetComponent<RectTransform>();
            kiosk.anchorMin = new Vector2(0f, 1f);
            kiosk.anchorMax = new Vector2(0f, 1f);
            kiosk.pivot = new Vector2(0f, 1f);
            kiosk.anchoredPosition = new Vector2(24f, -24f);
            kiosk.sizeDelta = new Vector2(Mathf.Min(430f, Screen.width * 0.34f), Screen.height - 48f);

            var viewportObject = new GameObject("DestinationViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(kiosk, false);
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(-12f, 0f);
            viewportObject.GetComponent<Image>().color = new Color(0.02f, 0.08f, 0.13f, 0.86f);

            var contentObject = new GameObject("DestinationContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewportObject.transform, false);
            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f);
            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollObject = new GameObject("DestinationScrollRect", typeof(RectTransform), typeof(ScrollRect));
            scrollObject.transform.SetParent(kiosk, false);
            scrollRect = scrollObject.GetComponent<ScrollRect>();
            viewportObject.transform.SetParent(scrollObject.transform, false);
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;
            var scrollTransform = scrollObject.GetComponent<RectTransform>();
            scrollTransform.anchorMin = new Vector2(0f, 1f);
            scrollTransform.anchorMax = new Vector2(0f, 1f);
            scrollTransform.pivot = new Vector2(0f, 1f);
            scrollTransform.anchoredPosition = new Vector2(24f, -181f);
            scrollTransform.sizeDelta = new Vector2(382f, 360f);

            var scrollbarObject = new GameObject("DestinationScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(kiosk, false);
            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbarObject.GetComponent<Image>().color = new Color(0.08f, 0.16f, 0.22f, 0.95f);
            var scrollbarTransform = scrollbarObject.GetComponent<RectTransform>();
            scrollbarTransform.anchorMin = new Vector2(0f, 1f);
            scrollbarTransform.anchorMax = new Vector2(0f, 1f);
            scrollbarTransform.pivot = new Vector2(0f, 1f);
            scrollbarTransform.anchoredPosition = new Vector2(24f + 370f, -181f);
            scrollbarTransform.sizeDelta = new Vector2(12f, 360f);
            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(scrollbarObject.transform, false);
            var handleTransform = handleObject.GetComponent<RectTransform>();
            handleTransform.anchorMin = Vector2.zero;
            handleTransform.anchorMax = Vector2.one;
            handleTransform.offsetMin = Vector2.zero;
            handleTransform.offsetMax = Vector2.zero;
            handleObject.GetComponent<Image>().color = new Color(0.35f, 0.95f, 1f, 1f);
            scrollbar.handleRect = handleTransform;
            scrollbar.targetGraphic = handleObject.GetComponent<Image>();
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            canvas.enabled = true;
        }

        private void AddRow(R057DestinationDefinition definition)
        {
            var row = new GameObject("DestinationButton_" + definition.id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(content, false);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 38f);
            row.GetComponent<LayoutElement>().preferredHeight = 38f;
            row.GetComponent<Image>().color = new Color(0.12f, 0.38f, 0.52f, 1f);
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(row.transform, false);
            var text = label.GetComponent<Text>();
            text.text = definition.displayName + "  [" + definition.category + "]";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(0.96f, 0.99f, 1f, 1f);
            text.alignment = TextAnchor.MiddleLeft;
            var labelTransform = label.GetComponent<RectTransform>();
            labelTransform.anchorMin = Vector2.zero;
            labelTransform.anchorMax = Vector2.one;
            labelTransform.offsetMin = new Vector2(12f, 0f);
            labelTransform.offsetMax = new Vector2(-8f, 0f);
            var selected = definition.displayName.Equals(controller.SelectedDestination, StringComparison.OrdinalIgnoreCase);
            row.GetComponent<Image>().color = selected ? new Color(0.05f, 0.75f, 0.95f, 1f) : new Color(0.12f, 0.38f, 0.52f, 1f);
            var captured = definition.displayName;
            row.GetComponent<Button>().onClick.AddListener(() => controller.SelectDestination(captured));
            rows.Add(row);
        }
    }
}
