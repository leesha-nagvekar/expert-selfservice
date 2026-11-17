// S-A (SPEC section 4): the secretariat's search. Plain JavaScript, no framework.
// - A tick in the sidebar applies straight away. The search box is in the same form, so its text is kept.
// - The Find box in a big group narrows its list as you type. It has no name, so it is never sent.
// - The form has no page field, so a tick always lands on page 1 of the results.
// - On a phone the filters start closed, unless they were open before the page reloaded.
// Without JavaScript the Search button applies everything and the Find boxes stay hidden.
(function () {
  var form = document.querySelector("form.search");
  if (!form) {
    return;
  }

  var focusKey = "staff-search-focus";
  var openKey = "staff-search-filters-open";

  function remember(key, value) {
    try {
      if (value === null) {
        sessionStorage.removeItem(key);
      } else {
        sessionStorage.setItem(key, value);
      }
    } catch (e) {
      // Storage can be blocked. The page works without it.
    }
  }

  function recall(key) {
    try {
      return sessionStorage.getItem(key);
    } catch (e) {
      return null;
    }
  }

  // Apply straight away. Remember the box, so focus comes back to it after the reload.
  form.addEventListener("change", function (event) {
    var box = event.target;
    if (box.type === "checkbox") {
      remember(focusKey, box.id);
      form.submit();
    }
  });

  document.querySelectorAll(".filters-apply").forEach(function (button) {
    button.hidden = true;
  });

  var focusId = recall(focusKey);
  remember(focusKey, null);
  var focusBox = focusId ? document.getElementById(focusId) : null;
  if (focusBox) {
    // Keep the group open, also when its last tick was just taken off (a group without ticks starts closed).
    var group = focusBox.closest("details.filter-group");
    if (group) {
      group.open = true;
    }
    focusBox.focus();
  }

  // Find boxes: hide the tick boxes whose label doesn't contain the text.
  document.querySelectorAll(".filter-find").forEach(function (find) {
    var group = find.closest(".filter-group");
    find.hidden = false;
    find.addEventListener("input", function () {
      var text = find.value.trim().toLocaleLowerCase();
      group.querySelectorAll(".choice").forEach(function (choice) {
        choice.hidden = text !== "" && choice.textContent.toLocaleLowerCase().indexOf(text) < 0;
      });
    });
    // Enter in a Find box would send the form. It only narrows the list.
    find.addEventListener("keydown", function (event) {
      if (event.key === "Enter") {
        event.preventDefault();
      }
    });
  });

  // Phone: the filters start closed, unless they were open before a tick reloaded the page.
  var filters = document.querySelector("details.filters");
  if (filters && window.matchMedia("(max-width: 899px)").matches) {
    filters.open = recall(openKey) === "1" || focusBox !== null;
    filters.addEventListener("toggle", function () {
      remember(openKey, filters.open ? "1" : null);
    });
  }
})();
