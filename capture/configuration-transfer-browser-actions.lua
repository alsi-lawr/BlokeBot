local actions = {}

actions.openImport = viset.javascript([=[
  () => {
    document.getElementById("configuration-transfer-mode-import-tab").click();
    return true;
  }
]=])

actions.loadDocument = viset.javascript([=[
  async ({ state, suffix }) => {
    const response = await fetch(
      "/configuration-transfer/export?sections=CustomCommands,Announcements,Guessing,Points,ChannelToolEnablement,Overlays,Automations&overlayUrls=true&overlayMedia=true&urlWarningAcknowledged=true"
    );
    const documentValue = await response.json();
    documentValue.source.blokeBotVersion = "0.12.0";

    if (state === "review") {
      const section = documentValue.sections.customCommands;
      const command = section.commands.find(value => value.action.type === "message");
      command.id = "command-import-review";
      command.name = `Welcome pack ${suffix}`;
      command.aliases = [`importwelcome${suffix}`];
      section.commands = [command];
      section.counters = [];
      documentValue.sections = { customCommands: section };
    } else if (state === "conflict") {
      const section = documentValue.sections.customCommands;
      const aliasConflict = section.commands.find(value => value.name === "Legacy fixed-route collision");
      const dependencyConflict = section.commands.find(value => value.action.type === "overlayCue");
      aliasConflict.id = "command-import-alias";
      dependencyConflict.id = "command-import-dependency";
      dependencyConflict.name = "Stream celebration";
      section.commands = [aliasConflict, dependencyConflict];
      section.counters = [];
      documentValue.sections = { customCommands: section };
    } else if (state === "success") {
      documentValue.sections.points.pointLabel = `stars-${suffix}`;
      documentValue.sections = { points: documentValue.sections.points };
    } else if (state === "failed") {
      const enablement = documentValue.sections.channelToolEnablement;
      await fetch("/simulation/collectives/disabled", { method: "POST" });
      enablement.collectives = true;
      documentValue.sections = { channelToolEnablement: enablement };
      await fetch("/simulation/configuration-activation/fail", { method: "POST" });
    }

    const textarea = document.getElementById("configuration-transfer-json");
    const setValue = Object.getOwnPropertyDescriptor(
      HTMLTextAreaElement.prototype,
      "value"
    ).set;
    setValue.call(textarea, JSON.stringify(documentValue));
    textarea.dispatchEvent(new Event("change", { bubbles: true }));
    return true;
  }
]=])

actions.prepareExport = viset.javascript([=[
  async ({ viewportWidth, viewportHeight }) => {
    const expectedSelections = [
      "Export Custom commands",
      "Export Announcements",
      "Export Guessing game",
      "Export Points & giveaways",
      "Export Chat Tools enablement",
      "Export Overlays",
      "Export Overlay URL layers",
      "Export Overlay media-document links",
      "Export Automations",
    ];
    for (const label of expectedSelections) {
      const toggle = document.querySelector(`button[aria-label="${label}"]`);
      if (toggle?.getAttribute("aria-checked") !== "true") {
        throw new Error(`Expected selected export control: ${label}`);
      }
    }

    const warning = document.querySelector(
      'button[aria-label="Confirm complete Overlay URL warning"]'
    );
    if (warning?.getAttribute("aria-checked") !== "true") warning?.click();
    window.scrollTo(0, 0);

    const settle = () => new Promise(resolve =>
      requestAnimationFrame(() => requestAnimationFrame(resolve))
    );
    await settle();

    const panel = document.querySelector(".task-panel");
    if (panel === null) throw new Error("The export panel is not available.");
    const originalBounds = panel.getBoundingClientRect();
    const visualWidth = originalBounds.width;
    const fits = bounds => bounds.bottom <= viewportHeight - 8;
    const applyScale = async scale => {
      panel.style.maxWidth = "none";
      panel.style.transform = `scale(${scale})`;
      panel.style.transformOrigin = "top left";
      panel.style.width = `${visualWidth / scale}px`;
      await settle();
      return panel.getBoundingClientRect();
    };

    let scale = 1;
    let panelBounds = originalBounds;
    if (!fits(panelBounds)) {
      let failingScale = 1;
      scale = 0.8;
      panelBounds = await applyScale(scale);
      while (!fits(panelBounds) && scale > 0.1) {
        failingScale = scale;
        scale *= 0.8;
        panelBounds = await applyScale(scale);
      }
      if (!fits(panelBounds)) {
        throw new Error("The complete export panel cannot fit in the viewport.");
      }

      for (let attempt = 0; attempt < 10; attempt++) {
        const candidate = (scale + failingScale) / 2;
        const candidateBounds = await applyScale(candidate);
        if (fits(candidateBounds)) {
          scale = candidate;
          panelBounds = candidateBounds;
        } else {
          failingScale = candidate;
        }
      }
      panelBounds = await applyScale(scale * 0.998);
    }

    window.scrollTo(0, 0);
    await settle();
    panelBounds = panel.getBoundingClientRect();
    const footer = panel.querySelector(".task-panel__footer");
    const visibleControls = expectedSelections
      .map(label => document.querySelector(`button[aria-label="${label}"]`))
      .concat(warning, footer, panel);
    if (visibleControls.some(element => {
      const bounds = element?.getBoundingClientRect();
      return bounds === undefined
        || bounds.top < 0
        || bounds.bottom > viewportHeight
        || bounds.left < 0
        || bounds.right > viewportWidth;
    })) {
      throw new Error("The complete export state does not fit in the viewport.");
    }
    if (Math.abs(panelBounds.width - visualWidth) > 2) {
      throw new Error("The fitted export panel did not preserve its usable width.");
    }

    const roleControl = document.querySelector(".account-menu__summary");
    const roleBounds = roleControl?.getBoundingClientRect();
    const roleIdentity = roleControl?.querySelector("span[title]");
    if (roleBounds === undefined
      || roleIdentity === null
      || roleBounds.left < 0
      || roleBounds.right > viewportWidth
      || roleIdentity.scrollWidth > roleIdentity.clientWidth) {
      throw new Error("The active channel role control is incomplete.");
    }
    return true;
  }
]=])

actions.previewImport = viset.javascript([=[
  () => {
    document.getElementById("configuration-transfer-preview").click();
    return true;
  }
]=])

actions.resolveConflicts = viset.javascript([=[
  () => {
    for (const select of document.querySelectorAll(".conflict-row select")) {
      const option = [...select.options].find(value => value.value !== "Unresolved");
      select.value = option.value;
      select.dispatchEvent(new Event("change", { bubbles: true }));
    }
    return true;
  }
]=])

actions.focusConflict = viset.javascript([=[
  ({ viewportWidth }) => {
    const target = document.querySelector(
      '.conflict-row, .page-state[data-page-state="failure"]'
    );
    target?.scrollIntoView({ block: "center", inline: "nearest" });
    const intendedTop = window.scrollY;
    window.scrollTo({ left: 0, top: intendedTop, behavior: "auto" });
    document.documentElement.scrollLeft = 0;
    document.body.scrollLeft = 0;

    const roleControl = document.querySelector(".account-menu__summary");
    const roleBounds = roleControl?.getBoundingClientRect();
    const roleIdentity = roleControl?.querySelector("span[title]");
    if (window.scrollX !== 0
      || Math.abs(window.scrollY - intendedTop) > 1
      || roleBounds === undefined
      || roleIdentity === null
      || roleBounds.left < 0
      || roleBounds.right > viewportWidth
      || roleIdentity.scrollWidth > roleIdentity.clientWidth) {
      throw new Error("The focused conflict state does not show the complete role control.");
    }
    return true;
  }
]=])

actions.selectEnablement = viset.javascript([=[
  () => {
    for (const toggle of document.querySelectorAll(".enablement-row .studio-toggle")) {
      if (toggle.getAttribute("aria-checked") !== "true") toggle.click();
    }
    return true;
  }
]=])

actions.applyImport = viset.javascript([=[
  () => {
    document.getElementById("configuration-transfer-apply").click();
    return true;
  }
]=])

return actions
