use std::ffi::CStr;
use std::os::raw::c_char;

/// Native core API version used by Dart FFI side to verify compatibility.
#[unsafe(no_mangle)]
pub extern "C" fn prismwave_core_api_version() -> u32 {
    1
}

/// Basic health check endpoint.
#[unsafe(no_mangle)]
pub extern "C" fn prismwave_ping() -> bool {
    true
}

/// Placeholder for load pipeline in V1 demo.
#[unsafe(no_mangle)]
pub extern "C" fn prismwave_load_track(path: *const c_char) -> bool {
    if path.is_null() {
        return false;
    }
    let c_path = unsafe { CStr::from_ptr(path) };
    !c_path.to_bytes().is_empty()
}

/// Placeholder playback control for V1 demo.
#[unsafe(no_mangle)]
pub extern "C" fn prismwave_play() -> bool {
    true
}

/// Placeholder playback control for V1 demo.
#[unsafe(no_mangle)]
pub extern "C" fn prismwave_pause() -> bool {
    true
}

/// Placeholder seek control for V1 demo.
#[unsafe(no_mangle)]
pub extern "C" fn prismwave_seek(_milliseconds: i64) -> bool {
    true
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::CString;

    #[test]
    fn api_version_should_be_v1() {
        assert_eq!(prismwave_core_api_version(), 1);
    }

    #[test]
    fn load_should_reject_null_pointer() {
        assert!(!prismwave_load_track(std::ptr::null()));
    }

    #[test]
    fn load_should_accept_non_empty_path() {
        let path = CString::new("C:\\music\\demo.mp3").expect("CString build");
        assert!(prismwave_load_track(path.as_ptr()));
    }
}
