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

  /* ---------- Modules ----------
     Availability (Included / Add-on / Not typical) is shown as a reference
     badge only, plans are for observation now, every module can be freely
     ticked on or off for this specific client regardless of what its plan
     normally includes. A module's checked state is only ever defaulted once,
     when it's first seen (Included modules start ticked, everything else
     starts unticked); after that it's entirely under manual control and
     survives switching between plan cards. */
  function moduleRow(module) {
    var availability = availabilityFor(module);
    var sel = state.selections[module.id];
    var included = availability === "Included";
    var notTypical = availability === "NotAvailable";

    var badge = included
      ? el("span", { class: "calc-badge calc-badge--included" }, [document.createTextNode("Included in plan")])
      : notTypical
        ? el("span", { class: "calc-badge calc-badge--optional" }, [document.createTextNode("Not typical for plan")])
        : el("span", { class: "calc-badge calc-badge--addon" }, [document.createTextNode("Add-on")]);

    var checkbox = el("input", {
      type: "checkbox",
      id: "calc-mod-" + module.id
    });
    checkbox.checked = sel.checked;
    checkbox.addEventListener("change", function () {
      sel.checked = checkbox.checked;
      volumeWrap.style.display = (sel.checked && module.chargeType === "Commission") ? "block" : "none";
      recalculate();
    });

    var priceHint = "";
    if (!included) {
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
      style: (sel.checked && module.chargeType === "Commission") ? "display:block" : "display:none"
    }, [
      el("label", {}, [document.createTextNode(module.volumeInputLabel || "Estimated monthly value (₹)")]),
      volumeInput
    ]);

    var row = el("div", { class: "calc-module-row" }, [
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
      if (!state.selections[module.id]) {
        state.selections[module.id] = { checked: availabilityFor(module) === "Included", volume: 0 };
      }
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
      selectedModules: selectedModules,
      waiveOneTimeSetupFees: !!(els.waiveSetup && els.waiveSetup.checked)
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
    if (result.oneTimeFeesWaived) {
      body.appendChild(summaryLine("One-time setup charges", "Waived"));
    } else if (result.oneTimeChargesTotal > 0) {
      body.appendChild(summaryLine("One-time setup charges", fmtMoney(result.oneTimeChargesTotal)));
    }

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

    renderPrintQuote(result);
  }

  /* ---------- Printed quotation ----------
     A formal itemised quotation (billto block, line-item tables, totals box,
     amount in words, terms, signature) built from the same calculate result
     the on-screen summary uses, nothing here is invented, every figure comes
     straight from the calculator's own API response. Rendered into the
     print-only containers added around the on-screen summary, which the
     @media print rules in style.css hide everything else in favour of. */
  var ONES = ["", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
    "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"];
  var TENS = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

  function threeDigitsToWords(n) {
    var out = "";
    if (n >= 100) { out += ONES[Math.floor(n / 100)] + " Hundred"; n %= 100; if (n) out += " "; }
    if (n >= 20) { out += TENS[Math.floor(n / 10)]; if (n % 10) out += " " + ONES[n % 10]; }
    else if (n > 0) { out += ONES[n]; }
    return out;
  }

  /** Indian numbering system (lakh/crore) rupee amount in words, e.g. 73264 -> "Seventy Three Thousand Two Hundred Sixty Four". */
  function amountToWordsIndian(amount) {
    var n = Math.round(Math.abs(Number(amount) || 0));
    if (n === 0) return "Zero";
    var crore = Math.floor(n / 10000000); n %= 10000000;
    var lakh = Math.floor(n / 100000); n %= 100000;
    var thousand = Math.floor(n / 1000); n %= 1000;
    var rest = n;
    var parts = [];
    if (crore) parts.push(threeDigitsToWords(crore) + " Crore");
    if (lakh) parts.push(threeDigitsToWords(lakh) + " Lakh");
    if (thousand) parts.push(threeDigitsToWords(thousand) + " Thousand");
    if (rest) parts.push(threeDigitsToWords(rest));
    return parts.join(" ");
  }

  function printTableRow(cells) {
    return el("tr", {}, cells.map(function (c) {
      return el("td", { class: c.class || "" }, [document.createTextNode(c.text)]);
    }));
  }

  /** One bordered, header-banded line-item table (mirrors the "Material /
      Products" and "Labor" tables in the reference quotation layout).
      Every row is fully pre-formatted text (row.cells = array of strings,
      one per column after Sl. No.), and row.amount is the raw number used
      only to sum the Total row, no formatting decisions happen in here. */
  function printItemTable(title, rows, columns, waived) {
    if (!rows.length) return null;
    var totalAmount = rows.reduce(function (sum, r) { return sum + r.amount; }, 0);

    var thead = el("thead", {}, [
      el("tr", {}, columns.map(function (c) { return el("th", { class: c.class || "" }, [document.createTextNode(c.label)]); }))
    ]);

    var tbody = el("tbody", {}, rows.map(function (r, i) {
      var cells = [{ text: String(i + 1), class: "calc-print-table__num" }, { text: r.description, class: "calc-print-table__desc" }];
      r.cells.forEach(function (c) { cells.push({ text: c, class: "calc-print-table__num" }); });
      cells.push({ text: waived ? "Waived" : fmtBase(r.amount), class: "calc-print-table__num calc-print-table__amount" });
      return printTableRow(cells);
    }));

    var blankTds = columns.length - 3; // Sl.No + Description + Amount already accounted for
    var tfoot = el("tfoot", {}, [
      el("tr", {}, [
        el("td", { colspan: "2", class: "calc-print-table__totallabel" }, [document.createTextNode("Total")])
      ].concat(
        Array.from({ length: blankTds }, function () { return el("td", {}, []); })
      ).concat([
        el("td", { class: "calc-print-table__num" }, [document.createTextNode(waived ? "Waived" : fmtBase(totalAmount))])
      ]))
    ]);

    return el("div", { class: "calc-print-table-wrap" }, [
      el("div", { class: "calc-print-table-title" }, [document.createTextNode(title)]),
      el("table", { class: "calc-print-table" }, [thead, tbody, tfoot])
    ]);
  }

  function renderPrintQuote(result) {
    var billToWrap = document.getElementById("calc-print-billto");
    var tablesWrap = document.getElementById("calc-print-tables");
    var totalsWrap = document.getElementById("calc-print-totals");
    var termsWrap = document.getElementById("calc-print-terms");
    if (!billToWrap || !tablesWrap || !totalsWrap || !termsWrap) return;

    billToWrap.innerHTML = "";
    tablesWrap.innerHTML = "";
    totalsWrap.innerHTML = "";
    termsWrap.innerHTML = "";

    if (!result.success) return;

    var customerName = (els.customer && els.customer.value.trim()) || "Prospective Customer";
    var rooms = els.rooms.value || "-";
    var props = els.properties.value || "-";

    /* ---- Bill To / quote meta ---- */
    billToWrap.appendChild(el("div", { class: "calc-print-billto" }, [
      el("div", { class: "calc-print-billto__col" }, [
        el("div", { class: "calc-print-billto__label" }, [document.createTextNode("Bill To")]),
        el("div", { class: "calc-print-billto__name" }, [document.createTextNode(customerName)]),
        el("div", {}, [document.createTextNode(props + " propert" + (props === "1" ? "y" : "ies") + " · " + rooms + " room" + (rooms === "1" ? "" : "s"))])
      ]),
      el("div", { class: "calc-print-billto__col calc-print-billto__col--right" }, [
        metaKeyVal("Quote No.", document.getElementById("calc-print-quoteno") ? document.getElementById("calc-print-quoteno").textContent : ""),
        metaKeyVal("Date", todayLabel()),
        metaKeyVal("Plan", planDisplayName()),
        metaKeyVal("Billing Cycle", result.billingCycleLabel)
      ])
    ]));

    /* ---- Line-item tables ---- */
    var subscriptionRows = [];
    var oneTimeRows = [];
    var commissionRows = [];

    result.lines.forEach(function (line) {
      if (line.oneTimeAmount > 0) {
        oneTimeRows.push({
          description: line.name + " (setup)",
          cells: ["1", fmtBase(line.oneTimeAmount), "-"],
          amount: line.oneTimeAmount
        });
      }

      if (line.lineType === "Commission") {
        commissionRows.push({
          description: line.name,
          cells: [fmtBase(line.volumeAmount), line.commissionPercent + "%", "-"],
          amount: line.monthlyAmount
        });
        return;
      }

      if (line.lineType === "Included") {
        subscriptionRows.push({
          description: line.name,
          cells: ["1", fmtBase(0), "-"],
          amount: 0
        });
        return;
      }

      // Base or AddOn: a real recurring monthly charge.
      subscriptionRows.push({
        description: line.name,
        cells: [String(line.quantity), fmtBase(line.unitPrice), result.taxRatePercent > 0 ? (result.taxRatePercent + "%") : "-"],
        amount: line.monthlyAmount
      });
    });

    var subscriptionTable = printItemTable("Subscription & Modules (Monthly)", subscriptionRows, [
      { label: "Sl. No.", class: "calc-print-table__num" },
      { label: "Description" },
      { label: "Qty", class: "calc-print-table__num" },
      { label: "Price / Unit (₹/mo)", class: "calc-print-table__num" },
      { label: "GST (%)", class: "calc-print-table__num" },
      { label: "Amount (₹/mo)", class: "calc-print-table__num" }
    ]);
    if (subscriptionTable) tablesWrap.appendChild(subscriptionTable);

    var commissionTable = printItemTable("Commission-Based Products (Estimated)", commissionRows, [
      { label: "Sl. No.", class: "calc-print-table__num" },
      { label: "Description" },
      { label: "Est. Volume (₹)", class: "calc-print-table__num" },
      { label: "Commission %", class: "calc-print-table__num" },
      { label: "GST (%)", class: "calc-print-table__num" },
      { label: "Amount (₹/mo, est.)", class: "calc-print-table__num" }
    ]);
    if (commissionTable) tablesWrap.appendChild(commissionTable);

    var oneTimeTable = printItemTable(
      "One-Time Setup Charges" + (result.oneTimeFeesWaived ? " (Waived)" : ""),
      oneTimeRows, [
        { label: "Sl. No.", class: "calc-print-table__num" },
        { label: "Description" },
        { label: "Qty", class: "calc-print-table__num" },
        { label: "Price / Unit (₹)", class: "calc-print-table__num" },
        { label: "GST (%)", class: "calc-print-table__num" },
        { label: result.oneTimeFeesWaived ? "Amount (₹, waived)" : "Amount (₹)", class: "calc-print-table__num" }
      ],
      result.oneTimeFeesWaived
    );
    if (oneTimeTable) tablesWrap.appendChild(oneTimeTable);

    /* ---- Amount in words + totals box, side by side like the reference layout ---- */
    var discountAmount = Math.max(0, (result.totalMonthlyCost * result.billingCycleMonths) - result.billingCycleRecurringTotal);
    var wordsBox = el("div", { class: "calc-print-words" }, [
      el("strong", {}, [document.createTextNode("Amount in Words: ")]),
      document.createTextNode("Rupees " + amountToWordsIndian(result.billingCycleTotalDue) + " Only")
    ]);
    var totalsBox = el("div", { class: "calc-print-totals" }, [
      totalsRow("Sub Total (" + result.billingCycleLabel + ")", fmtBase(result.totalMonthlyCost * result.billingCycleMonths)),
      discountAmount > 0 ? totalsRow("Billing Cycle Discount", "-" + fmtBase(discountAmount)) : null,
      oneTimeRows.length ? totalsRow("One-Time Setup Charges", result.oneTimeFeesWaived ? "Waived" : fmtBase(result.oneTimeChargesTotal)) : null,
      totalsRow("Final Amount Due", fmtBase(result.billingCycleTotalDue), true)
    ].filter(Boolean));
    totalsWrap.appendChild(el("div", { class: "calc-print-totals-row" }, [wordsBox, totalsBox]));

    /* ---- Terms ---- */
    var terms = [
      "This is an estimate generated by the eGlobe price calculator, final pricing is confirmed by our sales team before signup.",
      "Recurring charges shown are billed " + result.billingCycleLabel.toLowerCase() + (result.billingCycleDiscountPercent > 0 ? ", inclusive of the " + result.billingCycleDiscountPercent + "% billing-cycle discount" : "") + "."
    ];
    if (result.isCustomQuote) terms.push("Enterprise pricing is customised, the figures shown are indicative only.");
    termsWrap.appendChild(el("div", { class: "calc-print-terms" }, [
      el("div", { class: "calc-print-terms__label" }, [document.createTextNode("Terms & Conditions")]),
      el("ol", {}, terms.map(function (t) { return el("li", {}, [document.createTextNode(t)]); }))
    ]));
  }

  function metaKeyVal(label, value) {
    return el("div", { class: "calc-print-billto__row" }, [
      el("span", {}, [document.createTextNode(label)]),
      el("span", {}, [document.createTextNode(value)])
    ]);
  }

  function totalsRow(label, value, strong) {
    return el("div", { class: "calc-print-totals__row" + (strong ? " strong" : "") }, [
      el("span", {}, [document.createTextNode(label)]),
      el("span", {}, [document.createTextNode(value)])
    ]);
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
    els.waiveSetup = document.getElementById("calc-waive-setup");
    els.summaryBody = document.getElementById("calc-summary-body");
    var printBtn = document.getElementById("calc-print-btn");

    if (!els.planPicker) return; // not on this page

    [els.properties, els.rooms, els.customer].forEach(function (input) {
      input.addEventListener("input", recalculate);
    });
    if (els.waiveSetup) els.waiveSetup.addEventListener("change", recalculate);

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
      })
      .catch(function () {
        els.summaryBody.innerHTML = '<p class="calc-empty">Unable to load pricing right now, please refresh or try again shortly.</p>';
      });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
