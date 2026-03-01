"""Instrument generated C++ for stack overflow debugging."""
import sys


def add_trace(filename, func_sig, label):
    """Insert fprintf after function opening brace."""
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()

    # Build the trace line — use explicit bytes to avoid escape issues
    # We want:   fprintf(stderr, ">>> LABEL\n");
    trace_line = '    fprintf(stderr, ">>> ' + label + '\\n");\n'

    if trace_line in content:
        return  # Already instrumented

    idx = content.find(func_sig)
    if idx < 0:
        print(f"  WARNING: '{func_sig[:60]}...' not found in {filename}")
        return

    # Find the opening brace after the signature
    brace_idx = content.find('{', idx)
    if brace_idx < 0:
        return

    # Insert after the brace + newline
    newline_idx = content.find('\n', brace_idx)
    if newline_idx < 0:
        return

    content = content[:newline_idx + 1] + trace_line + content[newline_idx + 1:]

    with open(filename, 'w', encoding='utf-8') as f:
        f.write(content)
    print(f"  Instrumented {label} in {filename}")


def add_trace_before(filename, code_marker, label):
    """Insert fprintf before a specific line of code."""
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()

    trace_line = '    fprintf(stderr, ">>> ' + label + '\\n");\n'
    if trace_line in content:
        return

    idx = content.find(code_marker)
    if idx < 0:
        print(f"  WARNING before: '{code_marker[:60]}...' not found in {filename}")
        return

    # Find the start of the line containing code_marker
    line_start = content.rfind('\n', 0, idx)
    if line_start < 0:
        line_start = 0
    else:
        line_start += 1

    content = content[:line_start] + trace_line + content[line_start:]

    with open(filename, 'w', encoding='utf-8') as f:
        f.write(content)
    print(f"  Instrumented {label} (before) in {filename}")


def main():
    base = 'output/'

    # Function entry traces
    traces = [
        (base + 'HttpTest_methods_7.cpp',
         'void System_Net_Http_HttpClient__ctor(System_Net_Http_HttpClient* __this)',
         'HC.ctor'),
        (base + 'HttpTest_methods_11.cpp',
         'void System_Net_Http_HttpClientHandler__ctor(System_Net_Http_HttpClientHandler* __this)',
         'HCH.ctor'),
        (base + 'HttpTest_methods_8.cpp',
         'void System_Net_Http_SocketsHttpHandler__ctor(System_Net_Http_SocketsHttpHandler* __this)',
         'SHH.ctor'),
        (base + 'HttpTest_methods_4.cpp',
         'void System_Net_Http_HttpConnectionSettings__ctor(System_Net_Http_HttpConnectionSettings* __this)',
         'HCS.ctor'),
        (base + 'HttpTest_methods_4.cpp',
         'void System_Diagnostics_DistributedContextPropagator__cctor()',
         'DCP.cctor'),
        (base + 'HttpTest_methods_4.cpp',
         'System_Diagnostics_DistributedContextPropagator* System_Diagnostics_DistributedContextPropagator_CreateDefaultPropagator()',
         'DCP.CreateDefault'),
        (base + 'HttpTest_methods_4.cpp',
         'void System_Diagnostics_DistributedContextPropagator__ctor(System_Diagnostics_DistributedContextPropagator* __this)',
         'DCP.ctor'),
        (base + 'HttpTest_methods_2.cpp',
         'void System_Diagnostics_LegacyPropagator__cctor()',
         'LP.cctor'),
        (base + 'HttpTest_methods_2.cpp',
         'void System_Diagnostics_LegacyPropagator__ctor(System_Diagnostics_LegacyPropagator* __this)',
         'LP.ctor'),
        (base + 'HttpTest_methods_4.cpp',
         'System_Diagnostics_DistributedContextPropagator* System_Diagnostics_DistributedContextPropagator_get_Current()',
         'DCP.get_Current'),
        (base + 'HttpTest_methods_5.cpp',
         'void System_Net_Http_HttpMessageInvoker__ctor__System_Net_Http_HttpMessageHandler_System_Boolean(System_Net_Http_HttpMessageInvoker* __this, System_Net_Http_HttpMessageHandler* handler, bool disposeHandler)',
         'HMI.ctor'),
        (base + 'HttpTest_methods_7.cpp',
         'void System_Net_Http_HttpClient__ctor__System_Net_Http_HttpMessageHandler_System_Boolean(System_Net_Http_HttpClient* __this, System_Net_Http_HttpMessageHandler* handler, bool disposeHandler)',
         'HC.ctor3'),
        # New: trace more methods in the chain
        (base + 'HttpTest_methods_7.cpp',
         'void System_Net_Http_HttpClient__cctor()',
         'HC.cctor'),
        (base + 'HttpTest_methods_11.cpp',
         'void System_Net_Http_HttpClientHandler_set_ClientCertificateOptions(',
         'HCH.setCertOpts'),
    ]

    for filename, sig, label in traces:
        add_trace(filename, sig, label)

    # Before-line traces for specific calls in HC.ctor3
    add_trace_before(base + 'HttpTest_methods_7.cpp',
                     'System_Net_Http_HttpClient_ensure_cctor();\n    auto __t1 = System_Net_Http_HttpClient_statics.f_s_defaultTimeout',
                     'HC3.pre_cctor')
    add_trace_before(base + 'HttpTest_methods_7.cpp',
                     'System_Threading_CancellationTokenSource__ctor(__t2)',
                     'HC3.pre_CTS')

    print("Done.")


if __name__ == '__main__':
    main()
