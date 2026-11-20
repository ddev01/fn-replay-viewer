// @ts-check
// Small DOM helpers shared by the view modules.
import { state } from "./state.js";

/**
 * @param {string} id
 * @returns {HTMLElement}
 */
export const $ = (id) => {
  const el = document.getElementById(id);
  if (!el) throw new Error(`missing #${id}`);
  return el;
};

/**
 * The ids read through here are the page's checkboxes and search box.
 * @param {string} id
 * @returns {HTMLInputElement}
 */
export const $input = (id) => /** @type {HTMLInputElement} */ ($(id));

/** @returns {NodeListOf<HTMLButtonElement>} */
export function tabButtons() {
  return /** @type {NodeListOf<HTMLButtonElement>} */ (document.querySelectorAll(".tabs button"));
}

/**
 * Active state must reach assistive tech, not only the stylesheet.
 * @param {Element} el
 * @param {boolean} on
 */
export function setPressed(el, on) {
  el.classList.toggle("active", on);
  el.setAttribute("aria-pressed", String(on));
}

/** @returns {ScrollBehavior} */
export function scrollBehavior() {
  return matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth";
}

/**
 * @param {string} text
 * @param {number} [autoHideMs]
 */
export function showStatus(text, autoHideMs) {
  const el = $("status");
  el.hidden = !text;
  el.textContent = text || "";
  clearTimeout(state.statusTimer);
  if (text && autoHideMs) {
    state.statusTimer = setTimeout(() => showStatus(""), autoHideMs);
  }
}
