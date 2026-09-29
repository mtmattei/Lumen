cmake_minimum_required(VERSION 3.20)
project(lumen_rive CXX)

# Checkout of https://github.com/rive-app/rive-runtime (see build.sh / build.ps1 for the pinned commit).
set(RIVE_RUNTIME_DIR "${CMAKE_CURRENT_SOURCE_DIR}/.deps/rive-runtime" CACHE PATH "rive-runtime checkout")

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Core runtime only: no text, audio, layout or scripting (lumen-core.riv uses none of them).
file(GLOB_RECURSE RIVE_SOURCES CONFIGURE_DEPENDS "${RIVE_RUNTIME_DIR}/src/*.cpp")

add_library(lumen_rive SHARED ${RIVE_SOURCES} LumenRiveInterop.cpp)
target_include_directories(lumen_rive PRIVATE "${RIVE_RUNTIME_DIR}/include")
target_compile_definitions(lumen_rive PRIVATE _RIVE_INTERNAL_ YOGA_EXPORT= $<$<CONFIG:Release>:NDEBUG>)
if(MSVC)
  target_compile_options(lumen_rive PRIVATE /W0)
else()
  target_compile_options(lumen_rive PRIVATE -w)
endif()
