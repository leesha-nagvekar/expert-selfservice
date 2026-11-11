// X5 (SPEC section 4): when the postcode has 4 digits, ask the server for the city and show it.
// Plain JavaScript, no framework. Without JavaScript the "Find city" button does the same with a reload.
// The server checks the postcode again when the form is saved.
(function () {
  var input = document.getElementById("postcode");
  var city = document.getElementById("city");
  var find = document.getElementById("find-city");
  if (!input || !city) {
    return;
  }

  // The city appears by itself, so the fallback button isn't needed.
  if (find) {
    find.hidden = true;
  }

  var asked = input.value.trim();
  input.addEventListener("input", function () {
    var code = input.value.trim();
    if (code === asked) {
      return;
    }
    asked = code;
    if (!/^[0-9]{4}$/.test(code)) {
      city.textContent = "";
      return;
    }

    fetch("/postcode/" + code, { credentials: "same-origin", headers: { Accept: "application/json" } })
      .then(function (response) {
        return response.ok ? response.json() : null;
      })
      .then(function (data) {
        // Only the newest answer counts, if the expert typed on while waiting.
        if (asked === code) {
          city.textContent = data ? data.city : city.dataset.unknown;
        }
      })
      .catch(function () {
        city.textContent = "";
      });
  });
})();
