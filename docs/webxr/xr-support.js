export async function checkImmersiveArSupport(environment = globalThis) {
  if (!environment.isSecureContext) {
    return { supported: false, reason: "secure-context-required" };
  }
  const xr = environment.navigator && environment.navigator.xr;
  if (!xr || typeof xr.isSessionSupported !== "function" || typeof xr.requestSession !== "function") {
    return { supported: false, reason: "webxr-unavailable" };
  }
  try {
    const supported = await xr.isSessionSupported("immersive-ar");
    return supported
      ? { supported: true, reason: "available" }
      : { supported: false, reason: "device-or-browser-unsupported" };
  } catch (error) {
    return { supported: false, reason: "capability-check-failed", error };
  }
}

// Call this directly from the user's tap handler. WebXR requires a live user
// activation for requestSession(), so callers must not await another check first.
export function requestImmersiveArSession(environment = globalThis, options = {}) {
  const xr = environment.navigator && environment.navigator.xr;
  if (!environment.isSecureContext) {
    throw new Error("Immersive AR requires a secure browser context.");
  }
  if (!xr || typeof xr.requestSession !== "function") {
    throw new Error("WebXR immersive AR is unavailable in this browser.");
  }
  return xr.requestSession("immersive-ar", options);
}
