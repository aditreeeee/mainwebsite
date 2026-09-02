/* ==========================================================================
   Price Calculator (calculator.html)
   All pricing comes from the DB-driven /calculator/catalog + /calculator/calculate
   APIs. Nothing here hardcodes a rate, this file only renders and orchestrates.
   ========================================================================== */
(function () {
  "use strict";

  var state = {
    catalog: null,
    planType: null,
    taxId: null,
    billingCycleId: null,
    selections: {} // moduleId -> { checked, volume }
  };

  var els = {};

  function fmtMoney(n) {
    n = Number(n) || 0;
    return "₹" + n.toLocaleString("en-IN", { minimumFractionDigits: 0, maximumFractionDigits: 0 });
  }

  function el(tag, attrs, children) {
    var node = document.createElement(tag);
    attrs = attrs || {};
    Object.keys(attrs).forEach(function (k) {
      if (k === "class") node.className = attrs[k];
      else if (k === "html") node.innerHTML = attrs[k];
      else if (k.indexOf("on") === 0) node.addEventListener(k.slice(2), attrs[k]);
      else node.setAttribute(k, attrs[k]);
    });
    (children || []).forEach(function (c) { if (c) node.appendChild(c); });
    return node;
  }

  function availabilityFor(module) {
    return module.availability[state.planType] || "NotAvailable";
  }

  /* ---------- Plan picker: same visual language as pricing.html ---------- */
  function renderPlanPicker() {
    var wrap = els.planPicker;
    wrap.innerHTML = "";
    state.catalog.plans.forEach(function (plan, idx) {
      var active = plan.planType === state.planType;
      var priceLine = (plan.monthlyRatePerUnit ? fmtBase(plan.monthlyRatePerUnit) : "Custom quote") + " / month flat";
      var featuresList = el("ul", {}, [
        listItem(plan.unitDescription),
        listItem(plan.oneTimeSetupFee ? "One-time setup: " + fmtBase(plan.oneTimeSetupFee) : "No setup fee"),
        listItem(plan.isCustomQuote ? "Final pricing confirmed by sales" : "Same flat base at any size")
      ]);
      var card = el("button", {
        type: "button",
        class: "price-teaser-card calc-plan-select" + (active ? " featured" : ""),
        onclick: function () { selectPlan(plan.planType); }
      }, [
        el("span", { class: "badge-pill" }, [document.createTextNode(idx === 0 ? "Recommended" : plan.displayName)]),
        el("h4", {}, [document.createTextNode(plan.displayName)]),
        el("p", { class: "unit" }, [document.createTextNode(priceLine)]),
        featuresList
      ]);
      wrap.appendChild(card);
    });
  }

  function fmtBase(n) {
    return "₹" + (Number(n) || 0).toLocaleString("en-IN");
  }

  function listItem(text) {
    return el("li", {}, [
      (function () {
        var svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("fill", "none");
        svg.setAttribute("stroke", "currentColor");
        svg.setAttribute("stroke-width", "2");
        svg.innerHTML = '<polyline points="20 6 9 17 4 12"/>';
        return svg;
      })(),
      document.createTextNode(text)
    ]);
  }

  function selectPlan(planType) {
    state.planType = planType;
    renderPlanPicker();
    renderModules();
    recalculate();
  }

  /* ---------- Tax select ---------- */
  function renderTaxSelect() {
    els.tax.innerHTML = "";
    state.catalog.taxes.forEach(function (tax) {
      var opt = el("option", { value: tax.id }, [
        document.createTextNode(tax.name + " (" + tax.ratePercent + "%)")
      ]);
      if (tax.isDefault) { opt.selected = true; state.taxId = tax.id; }
      els.tax.appendChild(opt);
    });
    els.tax.addEventListener("change", function () {
      state.taxId = Number(els.tax.value);
      recalculate();
    });
  }

  function renderBillingCycleSelect() {
    els.billingCycle.innerHTML = "";
    state.catalog.billingCycles.forEach(function (cycle) {
      var label = cycle.label + (cycle.discountPercent > 0 ? " (" + cycle.discountPercent + "% off)" : "");
      var opt = el("option", { value: cycle.id }, [document.createTextNode(label)]);
      if (cycle.isDefault) { opt.selected = true; state.billingCycleId = cycle.id; }
      els.billingCycle.appendChild(opt);
    });
    els.billingCycle.addEventListener("change", function () {
      state.billingCycleId = Number(els.billingCycle.value);
      recalculate();
    });
  }

  /* ---------- Modules ---------- */
  function moduleRow(module) {
    var availability = availabilityFor(module);
    var sel = state.selections[module.id];
    var disabled = availability === "NotAvailable";
    var included = availability === "Included";

    if (included) sel.checked = true;
    if (disabled) sel.checked = false;

    var badge = included
      ? el("span", { class: "calc-badge calc-badge--included" }, [document.createTextNode("Included")])
      : disabled
        ? el("span", { class: "calc-badge calc-badge--disabled" }, [document.createTextNode("Not available")])
        : el("span", { class: "calc-badge calc-badge--addon" }, [document.createTextNode("Add-on")]);

    var checkbox = el("input", {
      type: "checkbox",
      id: "calc-mod-" + module.id
    });
    checkbox.checked = sel.checked;
    checkbox.disabled = disabled || included;
    checkbox.addEventListener("change", function () {
      sel.checked = checkbox.checked;
      volumeWrap.style.display = (sel.checked && module.chargeType === "Commission") ? "block" : "none";
      recalculate();
    });

    var priceHint = "";
    if (!disabled && !included) {
      if (module.chargeType === "Commission") {
        priceHint = module.commissionPercent + "% commission";
      } else if (module.chargeType === "PerRoomMonthly") {
        priceHint = fmtBase(module.monthlyRate) + " / room / mo";
      } else if (module.chargeType === "PerPropertyMonthly") {
        priceHint = fmtBase(module.monthlyRate) + " / property / mo";
      } else if (module.chargeType === "FlatMonthly") {
        priceHint = fmtBase(module.monthlyRate) + " / mo";
      }
      if (module.oneTimeSetupFee > 0) priceHint += (priceHint ? " + " : "") + fmtBase(module.oneTimeSetupFee) + " setup";
    }

    var volumeInput = el("input", {
      type: "number", min: "0", step: "1000",
      placeholder: module.volumeInputLabel || "Estimated volume (₹)"
    });
    volumeInput.value = sel.volume || "";
    volumeInput.addEventListener("input", function () {
      sel.volume = Number(volumeInput.value) || 0;
      recalculate();
    });

    var volumeWrap = el("div", {
      class: "calc-module-volume",
      style: (sel.checked && module.chargeType === "Commission" && !disabled) ? "display:block" : "display:none"
    }, [
      el("label", {}, [document.createTextNode(module.volumeInputLabel || "Estimated monthly value (₹)")]),
      volumeInput
    ]);

    var row = el("div", { class: "calc-module-row" + (disabled ? " is-disabled" : "") }, [
      el("label", { class: "calc-module-row__main", for: "calc-mod-" + module.id }, [
        checkbox,
        el("span", { class: "calc-module-row__name" }, [
          document.createTextNode(module.name + " "),
          module.tooltip ? el("span", { class: "calc-tooltip", title: module.tooltip }, [document.createTextNode("ⓘ")]) : null
        ]),
        badge,
        el("span", { class: "calc-module-row__price" }, [document.createTextNode(priceHint)])
      ]),
      volumeWrap
    ]);

    return row;
  }

  function renderModules() {
    var core = els.coreModules;
    var addon = els.addonModules;
    core.innerHTML = "";
    addon.innerHTML = "";

    state.catalog.modules.forEach(function (module) {
      if (!state.selections[module.id]) state.selections[module.id] = { checked: false, volume: 0 };
      var row = moduleRow(module);
      if (module.category === "CoreModule") core.appendChild(row);
      else addon.appendChild(row);
    });
  }

  function buildRequest() {
    var selectedModules = [];
    Object.keys(state.selections).forEach(function (id) {
      var sel = state.selections[id];
      if (sel.checked) selectedModules.push({ moduleId: Number(id), volumeAmount: sel.volume || 0 });
    });
    return {
      planType: state.planType,
      numberOfProperties: Number(els.properties.value) || 1,
      totalRooms: Number(els.rooms.value) || 1,
      taxId: state.taxId,
      billingCycleId: state.billingCycleId,
      selectedModules: selectedModules
    };
  }

  var debounceTimer = null;
  function recalculate() {
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(doCalculate, 200);
  }

  function doCalculate() {
    if (!state.planType) return;
    fetch("/calculator/calculate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(buildRequest())
    })
      .then(function (r) { return r.json(); })
      .then(renderSummary)
      .catch(function () {
        els.summaryBody.innerHTML = '<p class="calc-empty">Unable to calculate right now, please try again.</p>';
      });
  }

  function planDisplayName() {
    var plan = (state.catalog.plans || []).find(function (p) { return p.planType === state.planType; });
    return plan ? plan.displayName : state.planType;
  }

  function todayLabel() {
    return new Date().toLocaleDateString("en-IN", { day: "2-digit", month: "short", year: "numeric" });
  }

  function metaRow(label, value) {
    return el("div", { class: "calc-summary__meta-row" }, [
      el("span", {}, [document.createTextNode(label)]),
      el("span", {}, [document.createTextNode(value)])
    ]);
  }

  function renderMeta() {
    var customerName = (els.customer && els.customer.value.trim()) || "Prospective Customer";
    var rooms = els.rooms.value || "-";
    var props = els.properties.value || "-";
    return el("div", { class: "calc-summary__meta" }, [
      metaRow("Prepared for", customerName),
      metaRow("Plan", planDisplayName()),
      metaRow("Properties / Rooms", props + " / " + rooms),
      metaRow("Quote date", todayLabel())
    ]);
  }

  function summaryLine(label, value, opts) {
    opts = opts || {};
    return el("div", { class: "calc-summary__line" + (opts.strong ? " strong" : "") }, [
      el("span", {}, [document.createTextNode(label)]),
      el("span", {}, [document.createTextNode(value)])
    ]);
  }

  function renderSummary(result) {
    var body = els.summaryBody;
    body.innerHTML = "";

    if (!result.success) {
      body.appendChild(el("p", { class: "calc-empty" }, [document.createTextNode((result.errors || []).join(" ") || "Enter valid details to see a quote.")]));
      return;
    }

    body.appendChild(renderMeta());

    var linesWrap = el("div", { class: "calc-summary__lines" });
    result.lines.forEach(function (line) {
      if (line.lineType === "Ineligible") return;
      var amountText = line.lineType === "Included"
        ? "Included"
        : line.lineType === "Commission"
          ? fmtMoney(line.monthlyAmount) + "/mo (" + line.commissionPercent + "%)"
          : fmtMoney(line.monthlyAmount) + "/mo";
      linesWrap.appendChild(summaryLine(line.name, amountText));
    });
    body.appendChild(linesWrap);

    body.appendChild(el("div", { class: "calc-summary__divider" }));

    body.appendChild(summaryLine("Subscription subtotal", fmtMoney(result.subscriptionMonthlySubtotal) + "/mo"));
    if (result.taxAmount > 0) body.appendChild(summaryLine("Tax (" + result.taxRatePercent + "%)", fmtMoney(result.taxAmount)));
    if (result.commissionMonthlyEstimate > 0) body.appendChild(summaryLine("Commission (est.)", fmtMoney(result.commissionMonthlyEstimate) + "/mo"));
    if (result.oneTimeChargesTotal > 0) body.appendChild(summaryLine("One-time setup charges", fmtMoney(result.oneTimeChargesTotal)));

    body.appendChild(el("div", { class: "calc-summary__divider" }));

    body.appendChild(summaryLine("Total monthly cost", fmtMoney(result.totalMonthlyCost), { strong: true }));
    body.appendChild(summaryLine("Total annual cost", fmtMoney(result.totalAnnualCost), { strong: true }));

    body.appendChild(el("div", { class: "calc-summary__divider" }));

    var cycleTitle = result.billingCycleLabel + " billing"
      + (result.billingCycleDiscountPercent > 0 ? " (" + result.billingCycleDiscountPercent + "% off)" : "");
    body.appendChild(summaryLine(cycleTitle, fmtMoney(result.billingCycleRecurringTotal)));
    body.appendChild(summaryLine("Total due (incl. setup)", fmtMoney(result.billingCycleTotalDue), { strong: true }));

    if (result.effectiveCostPerRoom) body.appendChild(summaryLine("Effective cost / room / mo", fmtMoney(result.effectiveCostPerRoom)));
    if (result.effectiveCostPerProperty) body.appendChild(summaryLine("Effective cost / property / mo", fmtMoney(result.effectiveCostPerProperty)));

    if (result.isCustomQuote) {
      body.appendChild(el("p", { class: "calc-empty", style: "margin-top:14px;" }, [document.createTextNode("Enterprise pricing is customised, the figures above are an estimate. Contact sales to confirm final pricing.")]));
    }
  }

  function init() {
    els.planPicker = document.getElementById("calc-plan-picker");
    els.coreModules = document.getElementById("calc-core-modules");
    els.addonModules = document.getElementById("calc-addon-modules");
    els.properties = document.getElementById("calc-properties");
    els.rooms = document.getElementById("calc-rooms");
    els.customer = document.getElementById("calc-customer");
    els.tax = document.getElementById("calc-tax");
    els.billingCycle = document.getElementById("calc-billing-cycle");
    els.summaryBody = document.getElementById("calc-summary-body");
    var printBtn = document.getElementById("calc-print-btn");

    if (!els.planPicker) return; // not on this page

    [els.properties, els.rooms, els.customer].forEach(function (input) {
      input.addEventListener("input", recalculate);
    });

    if (printBtn) printBtn.addEventListener("click", function () { window.print(); });

    var quoteNoEl = document.getElementById("calc-print-quoteno");
    if (quoteNoEl) {
      var now = new Date();
      var pad = function (n) { return (n < 10 ? "0" : "") + n; };
      quoteNoEl.textContent = "Ref: EGS-" + now.getFullYear() + pad(now.getMonth() + 1) + pad(now.getDate())
        + "-" + pad(now.getHours()) + pad(now.getMinutes());
    }

    fetch("/calculator/catalog")
      .then(function (r) { return r.json(); })
      .then(function (catalog) {
        state.catalog = catalog;
        renderTaxSelect();
        renderBillingCycleSelect();
        var defaultPlan = catalog.plans[0] ? catalog.plans[0].planType : "PerRoom";
        selectPlan(defaultPlan);
      });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
