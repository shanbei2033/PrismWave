#include "flutter_window.h"

#include <algorithm>
#include <flutter_windows.h>
#include <optional>
#include <windowsx.h>

#include "flutter/generated_plugin_registrant.h"

namespace {

std::optional<LRESULT> HitTestResizeBorder(HWND hwnd, LPARAM lparam) {
  if (IsZoomed(hwnd)) {
    return std::nullopt;
  }

  RECT window_rect;
  if (!GetWindowRect(hwnd, &window_rect)) {
    return std::nullopt;
  }

  const POINT point = {GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam)};
  const HMONITOR monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
  const UINT dpi = FlutterDesktopGetDpiForMonitor(monitor);
  const int resize_border = std::max(10, MulDiv(16, dpi, 96));

  const bool on_left = point.x >= window_rect.left &&
                       point.x < window_rect.left + resize_border;
  const bool on_right = point.x <= window_rect.right &&
                        point.x > window_rect.right - resize_border;
  const bool on_top = point.y >= window_rect.top &&
                      point.y < window_rect.top + resize_border;
  const bool on_bottom = point.y <= window_rect.bottom &&
                         point.y > window_rect.bottom - resize_border;

  if (on_top && on_left) {
    return HTTOPLEFT;
  }
  if (on_top && on_right) {
    return HTTOPRIGHT;
  }
  if (on_bottom && on_left) {
    return HTBOTTOMLEFT;
  }
  if (on_bottom && on_right) {
    return HTBOTTOMRIGHT;
  }
  if (on_left) {
    return HTLEFT;
  }
  if (on_right) {
    return HTRIGHT;
  }
  if (on_top) {
    return HTTOP;
  }
  if (on_bottom) {
    return HTBOTTOM;
  }

  return std::nullopt;
}

}  // namespace

FlutterWindow::FlutterWindow(const flutter::DartProject& project)
    : project_(project) {}

FlutterWindow::~FlutterWindow() {}

bool FlutterWindow::OnCreate() {
  if (!Win32Window::OnCreate()) {
    return false;
  }

  RECT frame = GetClientArea();

  // The size here must match the window dimensions to avoid unnecessary surface
  // creation / destruction in the startup path.
  flutter_controller_ = std::make_unique<flutter::FlutterViewController>(
      frame.right - frame.left, frame.bottom - frame.top, project_);
  // Ensure that basic setup of the controller was successful.
  if (!flutter_controller_->engine() || !flutter_controller_->view()) {
    return false;
  }
  RegisterPlugins(flutter_controller_->engine());
  SetChildContent(flutter_controller_->view()->GetNativeWindow());

  // Show the native window immediately after the Flutter view is attached.
  // Waiting for the first frame can leave the app permanently hidden on some
  // Windows environments in release builds.
  this->Show();

  // Keep the original first-frame flow as a secondary path so Flutter is
  // guaranteed to schedule and present a frame after startup.
  flutter_controller_->engine()->SetNextFrameCallback([&]() {
    this->Show();
  });
  flutter_controller_->ForceRedraw();

  return true;
}

void FlutterWindow::OnDestroy() {
  if (flutter_controller_) {
    flutter_controller_ = nullptr;
  }

  Win32Window::OnDestroy();
}

LRESULT
FlutterWindow::MessageHandler(HWND hwnd, UINT const message,
                              WPARAM const wparam,
                              LPARAM const lparam) noexcept {
  if (message == WM_NCHITTEST) {
    const std::optional<LRESULT> hit_test = HitTestResizeBorder(hwnd, lparam);
    if (hit_test) {
      return *hit_test;
    }
  }

  // Give Flutter, including plugins, an opportunity to handle window messages.
  if (flutter_controller_) {
    std::optional<LRESULT> result =
        flutter_controller_->HandleTopLevelWindowProc(hwnd, message, wparam,
                                                      lparam);
    if (result) {
      return *result;
    }
  }

  switch (message) {
    case WM_FONTCHANGE:
      flutter_controller_->engine()->ReloadSystemFonts();
      break;
  }

  return Win32Window::MessageHandler(hwnd, message, wparam, lparam);
}
