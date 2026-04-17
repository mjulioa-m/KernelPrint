{{- define "kernelprint.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "kernelprint.fullname" -}}
{{- printf "%s-%s" .Release.Name (include "kernelprint.name" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}
