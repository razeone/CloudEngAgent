import { WidgetShell } from './WidgetShell';
import { fileDownloadProps } from '@/lib/widgets/schemas';
import type { WidgetRenderProps } from '@/lib/widgets/registry';

const API_BASE = import.meta.env.VITE_AGUI_API_BASE ?? '';

export function FileDownload({ state, runId }: WidgetRenderProps) {
  const parsed = fileDownloadProps.safeParse(state.props);
  const filename = parsed.success ? parsed.data.filename : 'artifact.bin';
  const desc = parsed.success ? parsed.data.description : undefined;
  const artifactId = state.artifact?.id;
  const href = artifactId
    ? `${API_BASE}/v1/runs/${runId}/artifacts/${artifactId}/download`
    : '#';
  return (
    <WidgetShell state={state}>
      <a className="text-primary underline" href={href} download={filename}>
        {filename}
      </a>
      {desc ? <p className="mt-1">{desc}</p> : null}
    </WidgetShell>
  );
}
