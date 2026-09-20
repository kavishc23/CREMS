import { useEffect, useState } from "react";
import AddOutlined from "@mui/icons-material/AddOutlined";
import DeleteOutline from "@mui/icons-material/DeleteOutline";
import RuleOutlined from "@mui/icons-material/RuleOutlined";
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  FormControl,
  FormControlLabel,
  Grid,
  IconButton,
  InputLabel,
  MenuItem,
  Pagination,
  Select,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import { api } from "../api/client";
import { PageHeader } from "../components/PageHeader";

type ScopeItem = { id: string; name: string };
type Stage = {
  sequence: number;
  name: string;
  assignedRole: string;
  assignedUserId: null;
  escalateAfterHours: number;
  escalationRole: string | null;
};
type Option = { value: string; label: string };
type ConditionOptions = {
  assetTypes: Option[];
  durationOperators: Option[];
  matchModes: Option[];
};
type Rule = {
  id: string;
  name: string;
  type: string;
  entityType: string | null;
  branchId: string | null;
  divisionId: string | null;
  minimumAmount: number | null;
  triggerForEquipment: boolean;
  assetTypeCondition: string | null;
  hireDurationOperator: string | null;
  hireDurationDays: number | null;
  conditionMatchMode: "Any" | "All";
  triggerForPersonnel: boolean;
  triggerForOvertime: boolean;
  isDefaultForBookings: boolean;
  appliesToBooking: boolean;
  appliesToQuotation: boolean;
  priority: number;
  isActive: boolean;
  conditionSummary: string;
  stages: Stage[];
};
type Form = Omit<Rule, "id">;
type ConditionKind =
  | "assetType"
  | "duration"
  | "value"
  | "equipment"
  | "personnel"
  | "overtime"
  | "everyBooking";
const conditionLabels: Record<ConditionKind, string> = {
  assetType: "Asset type",
  duration: "Hire duration (days)",
  value: "Request value (FJD)",
  equipment: "Equipment hire",
  personnel: "Operator / personnel",
  overtime: "Staff overtime",
  everyBooking: "Every booking",
};
const standardStages: Stage[] = [
  {
    sequence: 1,
    name: "Rental officer review",
    assignedRole: "RentalOfficer",
    assignedUserId: null,
    escalateAfterHours: 12,
    escalationRole: "BranchManager",
  },
  {
    sequence: 2,
    name: "Branch manager approval",
    assignedRole: "BranchManager",
    assignedUserId: null,
    escalateAfterHours: 24,
    escalationRole: "BranchManager",
  },
];
const blank = (): Form => ({
  name: "",
  type: "Booking",
  entityType: "Booking",
  branchId: null,
  divisionId: null,
  minimumAmount: null,
  triggerForEquipment: false,
  assetTypeCondition: null,
  hireDurationOperator: null,
  hireDurationDays: null,
  conditionMatchMode: "Any",
  triggerForPersonnel: false,
  triggerForOvertime: false,
  isDefaultForBookings: false,
  appliesToBooking: true,
  appliesToQuotation: false,
  priority: 0,
  isActive: true,
  conditionSummary: "",
  stages: standardStages.map((x) => ({ ...x })),
});
const roleLabel = (role: string | null) =>
  role === "RentalOfficer"
    ? "Rental officer"
    : role === "BranchManager"
      ? "Branch manager"
      : "Unassigned";
const operatorSymbol = (value: string | null) =>
  value === "GreaterThan"
    ? ">"
    : value === "GreaterThanOrEqual"
      ? "≥"
      : value === "LessThan"
        ? "<"
        : value === "LessThanOrEqual"
          ? "≤"
          : "";
const splitWords = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2");
const conditionKindsFor = (rule: Form): ConditionKind[] =>
  ([
    rule.assetTypeCondition && "assetType",
    rule.hireDurationOperator && "duration",
    rule.minimumAmount != null && "value",
    rule.triggerForEquipment && "equipment",
    rule.triggerForPersonnel && "personnel",
    rule.triggerForOvertime && "overtime",
    rule.isDefaultForBookings && "everyBooking",
  ].filter(Boolean) as ConditionKind[]);
function triggerLabel(rule: Form, preferSummary = true) {
  if (preferSummary && rule.conditionSummary) return rule.conditionSummary;
  const values = [
    rule.triggerForEquipment && "equipment hire",
    rule.assetTypeCondition &&
      `asset type is ${splitWords(rule.assetTypeCondition)}`,
    rule.hireDurationOperator &&
      rule.hireDurationDays != null &&
      `item hire duration ${operatorSymbol(rule.hireDurationOperator)} ${rule.hireDurationDays} days`,
    rule.triggerForPersonnel && "operator or personnel included",
    rule.triggerForOvertime && "overtime included",
    rule.minimumAmount != null &&
      `booking value ≥ FJD ${rule.minimumAmount.toLocaleString()}`,
    rule.isDefaultForBookings && "every booking",
  ].filter(Boolean);
  return values.length
    ? values.join(rule.conditionMatchMode === "All" ? " AND " : " OR ")
    : "No condition configured";
}

export function BookingApprovalRulesPage() {
  const [rules, setRules] = useState<Rule[]>([]),
    [divisions, setDivisions] = useState<ScopeItem[]>([]),
    [branches, setBranches] = useState<ScopeItem[]>([]),
    [options, setOptions] = useState<ConditionOptions>({
      assetTypes: [],
      durationOperators: [],
      matchModes: [],
    });
  const [editing, setEditing] = useState<string | null>(null),
    [form, setForm] = useState<Form>(blank()),
    [conditionKinds, setConditionKinds] = useState<ConditionKind[]>([]),
    [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [saving, setSaving] = useState(false),
    [ruleSearch, setRuleSearch] = useState(""),
    [ruleStatus, setRuleStatus] = useState<"all" | "active" | "inactive">(
      "all",
    ),
    [rulePage, setRulePage] = useState(1);
  async function load() {
    try {
      const [r, d, b, o] = await Promise.all([
        api.get<Rule[]>("/approval-workflows"),
        api.get<ScopeItem[]>("/divisions"),
        api.get<ScopeItem[]>("/branches"),
        api.get<ConditionOptions>(
          "/approval-workflows/booking-condition-options",
        ),
      ]);
      setRules(
        r.data.map((x) => ({
            ...x,
            appliesToBooking: x.appliesToBooking ?? true,
            appliesToQuotation: x.appliesToQuotation ?? false,
            conditionMatchMode: x.conditionMatchMode ?? "Any",
            stages: x.stages.map((stage) =>
              stage.assignedRole === "RentalOfficer"
                ? stage
                : {
                    ...stage,
                    assignedRole: "BranchManager",
                    escalationRole: "BranchManager",
                  },
            ),
          })),
      );
      setDivisions(d.data);
      setBranches(b.data);
      setOptions(o.data);
      setError("");
    } catch {
      setError("Approval rules could not be loaded.");
    }
  }
  useEffect(() => {
    void load();
  }, []);
  function reset() {
    setEditing(null);
    setForm(blank());
    setConditionKinds([]);
    setNotice("");
  }
  function edit(x: Rule) {
    setEditing(x.id);
    setForm({
      ...x,
      conditionSummary: "",
      stages: [...x.stages]
        .sort((a, b) => a.sequence - b.sequence)
        .map((stage) =>
          stage.assignedRole === "RentalOfficer"
            ? stage
            : {
                ...stage,
                assignedRole: "BranchManager",
                escalationRole: "BranchManager",
              },
        ),
    });
    setConditionKinds(conditionKindsFor(x));
    setNotice("");
  }
  function updateStage(i: number, p: Partial<Stage>) {
    setForm((f) => ({
      ...f,
      stages: f.stages.map((x, n) => (n === i ? { ...x, ...p } : x)),
    }));
  }
  function addStage() {
    setForm((f) => ({
      ...f,
      stages: [
        ...f.stages,
        {
          sequence: f.stages.length + 1,
          name: "Branch manager approval",
          assignedRole: "BranchManager",
          assignedUserId: null,
          escalateAfterHours: 24,
          escalationRole: "BranchManager",
        },
      ],
    }));
  }
  function removeStage(i: number) {
    setForm((f) => ({
      ...f,
      stages: f.stages
        .filter((_, n) => n !== i)
        .map((x, n) => ({ ...x, sequence: n + 1 })),
    }));
  }
  function usePreset(kind: "equipment" | "overtime" | "highValue") {
    setEditing(null);
    setError("");
    setNotice("");
    const base = blank();
    setForm(
      kind === "equipment"
        ? {
            ...base,
            name: "Equipment and professional personnel",
            triggerForEquipment: true,
            triggerForPersonnel: true,
            priority: 300,
          }
        : kind === "overtime"
          ? {
              ...base,
              name: "Staff overtime approval",
              triggerForOvertime: true,
              priority: 250,
            }
          : {
              ...base,
              name: "High-value rental approval",
              minimumAmount: 5000,
              priority: 200,
            },
    );
    setConditionKinds(
      kind === "equipment"
        ? ["equipment", "personnel"]
        : kind === "overtime"
          ? ["overtime"]
          : ["value"],
    );
  }
  function clearCondition(kind: ConditionKind, source: Form): Form {
    if (kind === "assetType") return { ...source, assetTypeCondition: null };
    if (kind === "duration") return { ...source, hireDurationOperator: null, hireDurationDays: null };
    if (kind === "value") return { ...source, minimumAmount: null };
    if (kind === "equipment") return { ...source, triggerForEquipment: false };
    if (kind === "personnel") return { ...source, triggerForPersonnel: false };
    if (kind === "overtime") return { ...source, triggerForOvertime: false };
    return { ...source, isDefaultForBookings: false };
  }
  function seedCondition(kind: ConditionKind, source: Form): Form {
    if (kind === "assetType") return { ...source, assetTypeCondition: options.assetTypes[0]?.value ?? null };
    if (kind === "duration") return { ...source, hireDurationOperator: "GreaterThan", hireDurationDays: 7 };
    if (kind === "value") return { ...source, minimumAmount: 5000 };
    if (kind === "equipment") return { ...source, triggerForEquipment: true };
    if (kind === "personnel") return { ...source, triggerForPersonnel: true };
    if (kind === "overtime") return { ...source, triggerForOvertime: true };
    return { ...source, isDefaultForBookings: true };
  }
  function replaceCondition(index: number, next: ConditionKind) {
    setForm((current) => seedCondition(next, clearCondition(conditionKinds[index], current)));
    setConditionKinds((current) => current.map((kind, i) => (i === index ? next : kind)));
  }
  function removeCondition(index: number) {
    setForm((current) => clearCondition(conditionKinds[index], current));
    setConditionKinds((current) => current.filter((_, i) => i !== index));
  }
  function addCondition() {
    const next = (Object.keys(conditionLabels) as ConditionKind[]).find((kind) => !conditionKinds.includes(kind));
    if (!next) return;
    setForm((current) => seedCondition(next, current));
    setConditionKinds((current) => [...current, next]);
  }
  async function save() {
    if (!form.name.trim()) {
      setError("Enter a rule name.");
      return;
    }
    if (form.stages.length === 0) {
      setError("Add at least one approval stage.");
      return;
    }
    if (!form.appliesToBooking && !form.appliesToQuotation) {
      setError("Select rental booking, quotation request, or both.");
      return;
    }
    if (
      (form.hireDurationOperator !== null) !==
      (form.hireDurationDays != null && form.hireDurationDays > 0)
    ) {
      setError(
        "Choose a duration operator and enter a positive number of days together.",
      );
      return;
    }
    if (
      !form.isDefaultForBookings &&
      form.minimumAmount == null &&
      !form.triggerForEquipment &&
      !form.assetTypeCondition &&
      !form.hireDurationOperator &&
      !form.triggerForPersonnel &&
      !form.triggerForOvertime
    ) {
      setError(
        "Choose at least one condition, or apply the rule to every booking.",
      );
      return;
    }
    setSaving(true);
    try {
      const payload = {
        ...form,
        stages: form.stages.map((x, i) => ({ ...x, sequence: i + 1 })),
      };
      editing
        ? await api.put(`/approval-workflows/${editing}`, payload)
        : await api.post("/approval-workflows", payload);
      reset();
      setNotice("Approval rule saved.");
      await load();
    } catch {
      setError("The approval rule could not be saved.");
    } finally {
      setSaving(false);
    }
  }
  async function removeRule(rule: Rule) {
    if (!window.confirm(`Delete “${rule.name}”? This cannot be undone.`))
      return;
    setSaving(true);
    setError("");
    try {
      await api.delete(`/approval-workflows/${rule.id}`);
      if (editing === rule.id) reset();
      setNotice("Approval rule deleted.");
      await load();
    } catch (reason: any) {
      setError(
        reason?.response?.data?.message ??
          "The approval rule could not be deleted.",
      );
    } finally {
      setSaving(false);
    }
  }
  const visibleRules = rules.filter((rule) => {
    const query = ruleSearch.trim().toLowerCase();
    const division = divisions.find((x) => x.id === rule.divisionId)?.name ?? "all divisions";
    const branch = branches.find((x) => x.id === rule.branchId)?.name ?? "all branches";
    const requestType = rule.appliesToBooking && rule.appliesToQuotation
      ? "booking quotation both"
      : rule.appliesToQuotation ? "quotation" : "booking";
    return (
      (ruleStatus === "all" || ruleStatus === (rule.isActive ? "active" : "inactive")) &&
      [rule.name, requestType, division, branch, triggerLabel(rule)]
        .join(" ")
        .toLowerCase()
        .includes(query)
    );
  });
  const rulesPerPage = 6;
  const totalRulePages = Math.max(
    1,
    Math.ceil(visibleRules.length / rulesPerPage),
  );
  const selectedRulePage = Math.min(rulePage, totalRulePages);
  const displayedRules = visibleRules.slice(
    (selectedRulePage - 1) * rulesPerPage,
    selectedRulePage * rulesPerPage,
  );
  const requestTypeLabel = (rule: Pick<Rule, "appliesToBooking" | "appliesToQuotation">) =>
    rule.appliesToBooking && rule.appliesToQuotation
      ? "Booking and quotation"
      : rule.appliesToQuotation
        ? "Quotation"
        : "Booking";
  return (
    <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1500, mx: "auto" }}>
      <PageHeader
        icon={<RuleOutlined />}
        title="Approval rules"
        subtitle="Choose when a request needs review and who makes the decision."
        actions={
          <Button
            variant="outlined"
            startIcon={<AddOutlined />}
            onClick={reset}
          >
            Create custom rule
          </Button>
        }
      />
      {error && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError("")}>
          {error}
        </Alert>
      )}
      {notice && (
        <Alert severity="success" sx={{ mb: 2 }}>
          {notice}
        </Alert>
      )}
      <Alert severity="info" sx={{ mb: 3 }}>
        <Typography fontWeight={750}>Approval path</Typography>
        <strong>Normal rental:</strong> Rental officer approval only.{" "}
        <strong>Matching exception:</strong> Rental officer review → Branch
        manager approval. Choose whether all configured conditions or any one
        condition must match. With AND, asset type and duration must match the
        same booking item. Scope specificity wins before rule order.
      </Alert>
      <Grid container spacing={3} alignItems="flex-start">
        <Grid size={{ xs: 12, lg: 5 }}>
          <Card variant="outlined">
            <CardContent>
              <Stack spacing={1.5}>
                <Stack direction="row" alignItems="center" gap={1}>
                  <Typography variant="h6" fontWeight={850}>Configured rules</Typography>
                  <Chip size="small" label={rules.length} />
                </Stack>
                <Box>
                  <Typography fontWeight={800}>Quick rule templates</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Choose a template, adjust its scope if needed, then save it.
                  </Typography>
                </Box>
                <Stack direction="row" gap={1} flexWrap="wrap">
                  <Button
                    size="small"
                    variant="outlined"
                    onClick={() => usePreset("equipment")}
                  >
                    Equipment / personnel
                  </Button>
                  <Button
                    size="small"
                    variant="outlined"
                    onClick={() => usePreset("overtime")}
                  >
                    Staff overtime
                  </Button>
                  <Button
                    size="small"
                    variant="outlined"
                    onClick={() => usePreset("highValue")}
                  >
                    High value
                  </Button>
                </Stack>
                <Stack
                  direction={{ xs: "column", sm: "row" }}
                  justifyContent="space-between"
                  alignItems={{ sm: "center" }}
                  gap={1}
                  mt={1}
                >
                  <Stack direction="row" gap={1}>
                    <TextField
                      size="small"
                      label="Find rule"
                      value={ruleSearch}
                      onChange={(event) => {
                        setRuleSearch(event.target.value);
                        setRulePage(1);
                      }}
                      sx={{ minWidth: 150 }}
                    />
                    <FormControl size="small" sx={{ minWidth: 120 }}>
                      <Select
                        value={ruleStatus}
                        onChange={(event) => {
                          setRuleStatus(
                            event.target.value as "all" | "active" | "inactive",
                          );
                          setRulePage(1);
                        }}
                      >
                        <MenuItem value="all">All rules</MenuItem>
                        <MenuItem value="active">Active</MenuItem>
                        <MenuItem value="inactive">Inactive</MenuItem>
                      </Select>
                    </FormControl>
                  </Stack>
                </Stack>
                {rules.length === 0 && (
                  <Alert severity="info">
                    No approval rules have been configured. Rental officers can
                    confirm routine bookings.
                  </Alert>
                )}
                {rules.length > 0 && visibleRules.length === 0 && (
                  <Alert severity="info">No rules match this filter.</Alert>
                )}
                <Stack spacing={1.5}>
                  {displayedRules.map((x) => (
                    <Card
                      key={x.id}
                      variant="outlined"
                      onClick={() => edit(x)}
                      sx={{
                        cursor: "pointer",
                        opacity: x.isActive ? 1 : 0.72,
                        borderColor:
                          editing === x.id ? "secondary.main" : "divider",
                        bgcolor:
                          editing === x.id
                            ? "rgba(255,232,0,.06)"
                            : "background.paper",
                      }}
                    >
                      <CardContent>
                        <Stack
                          direction="row"
                          justifyContent="space-between"
                          gap={1}
                        >
                          <Box>
                            <Typography fontWeight={850}>{x.name}</Typography>
                            <Typography
                              variant="caption"
                              color="text.secondary"
                            >
                              {requestTypeLabel(x)}{" · "}
                              {x.divisionId
                                ? divisions.find((d) => d.id === x.divisionId)
                                    ?.name
                                : "All divisions"}{" "}
                              ·{" "}
                              {x.branchId
                                ? branches.find((b) => b.id === x.branchId)
                                    ?.name
                                : "All branches"}{" "}
                              · Order {x.priority}
                            </Typography>
                          </Box>
                          <Stack direction="row" gap={0.5} alignItems="center">
                            <Chip
                              size="small"
                              variant="outlined"
                              label={
                                x.conditionMatchMode === "All"
                                  ? "ALL · AND"
                                  : "ANY · OR"
                              }
                            />
                            <Chip
                              size="small"
                              label={x.isActive ? "Active" : "Inactive"}
                              color={x.isActive ? "success" : "default"}
                            />
                            <Tooltip title="Delete rule">
                              <IconButton
                                size="small"
                                color="error"
                                aria-label={`Delete ${x.name}`}
                                disabled={saving}
                                onClick={(event) => {
                                  event.stopPropagation();
                                  void removeRule(x);
                                }}
                              >
                                <DeleteOutline fontSize="small" />
                              </IconButton>
                            </Tooltip>
                          </Stack>
                        </Stack>
                        <Box
                          sx={{
                            mt: 1.5,
                            p: 1.25,
                            borderRadius: 1.5,
                            bgcolor: "grey.100",
                          }}
                        >
                          <Typography variant="caption" color="text.secondary">
                            WHEN
                          </Typography>
                          <Typography variant="body2" fontWeight={700}>
                            {triggerLabel(x)}
                          </Typography>
                        </Box>
                        <Stack
                          direction="row"
                          gap={0.75}
                          alignItems="center"
                          flexWrap="wrap"
                          mt={1.5}
                        >
                          {[...x.stages]
                            .sort((a, b) => a.sequence - b.sequence)
                            .map((stage, index) => (
                              <Box
                                key={stage.sequence}
                                sx={{ display: "contents" }}
                              >
                                {index > 0 && (
                                  <Typography color="text.secondary">
                                    →
                                  </Typography>
                                )}
                                <Chip
                                  size="small"
                                  variant="outlined"
                                  label={`${stage.sequence}. ${roleLabel(stage.assignedRole)}`}
                                />
                              </Box>
                            ))}
                        </Stack>
                      </CardContent>
                    </Card>
                  ))}
                </Stack>
                {totalRulePages > 1 && (
                  <Stack direction="row" justifyContent="center">
                    <Pagination
                      count={totalRulePages}
                      page={selectedRulePage}
                      onChange={(_, page) => setRulePage(page)}
                      color="primary"
                      size="small"
                    />
                  </Stack>
                )}
              </Stack>
            </CardContent>
          </Card>
        </Grid>
        <Grid size={{ xs: 12, lg: 7 }}>
          <Card variant="outlined">
            <CardContent>
              <Typography variant="h6" fontWeight={850} mb={2}>
                {editing ? "Edit rule" : "Create rule"}
              </Typography>
              <Stack direction="row" alignItems="center" gap={1} mb={1.5}>
                <Chip label="1" color="secondary" size="small" />
                <Box>
                  <Typography fontWeight={800}>
                    What does this rule cover?
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    Give the policy a clear name and choose where it applies.
                  </Typography>
                </Box>
              </Stack>
              <Grid container spacing={2}>
                <Grid size={12}>
                  <TextField
                    fullWidth
                    label="Rule name"
                    value={form.name}
                    onChange={(e) => setForm({ ...form, name: e.target.value })}
                    placeholder="Heavy equipment and overtime"
                  />
                </Grid>
                <Grid size={{ xs: 12, sm: 6 }}>
                  <FormControl fullWidth>
                    <InputLabel>Request type</InputLabel>
                    <Select
                      label="Request type"
                      value={
                        form.appliesToBooking && form.appliesToQuotation
                          ? "Both"
                          : form.appliesToQuotation
                            ? "Quotation"
                            : "Booking"
                      }
                      onChange={(event) => {
                        const selected = event.target.value;
                        setForm({
                          ...form,
                          appliesToBooking: selected === "Booking" || selected === "Both",
                          appliesToQuotation: selected === "Quotation" || selected === "Both",
                        });
                      }}
                    >
                      <MenuItem value="Booking">Rental booking</MenuItem>
                      <MenuItem value="Quotation">Quotation request</MenuItem>
                      <MenuItem value="Both">Both</MenuItem>
                    </Select>
                  </FormControl>
                </Grid>
                <Grid size={{ xs: 12, sm: 6 }}>
                  <FormControl fullWidth>
                    <InputLabel>Division scope</InputLabel>
                    <Select
                      label="Division scope"
                      value={form.divisionId ?? ""}
                      onChange={(e) =>
                        setForm({ ...form, divisionId: e.target.value || null })
                      }
                    >
                      <MenuItem value="">All divisions</MenuItem>
                      {divisions.map((x) => (
                        <MenuItem key={x.id} value={x.id}>
                          {x.name}
                        </MenuItem>
                      ))}
                    </Select>
                  </FormControl>
                </Grid>
                <Grid size={{ xs: 12, sm: 6 }}>
                  <FormControl fullWidth>
                    <InputLabel>Branch scope</InputLabel>
                    <Select
                      label="Branch scope"
                      value={form.branchId ?? ""}
                      onChange={(e) =>
                        setForm({ ...form, branchId: e.target.value || null })
                      }
                    >
                      <MenuItem value="">All branches</MenuItem>
                      {branches.map((x) => (
                        <MenuItem key={x.id} value={x.id}>
                          {x.name}
                        </MenuItem>
                      ))}
                    </Select>
                  </FormControl>
                </Grid>
              </Grid>
              <Box
                mt={2}
                p={1.5}
                border="1px solid"
                borderColor="divider"
                borderRadius={2}
              >
                <Stack direction="row" alignItems="center" gap={1} mb={0.5}>
                  <Chip label="2" color="secondary" size="small" />
                  <Typography fontWeight={800}>When does it apply?</Typography>
                </Stack>
                <Typography variant="body2" color="text.secondary" mb={1.5}>
                  One day is exactly 24 hours. Asset type and duration are
                  evaluated against each booking item.
                </Typography>
                <FormControl fullWidth size="small" sx={{ mb: 1.5 }}>
                  <InputLabel>Request must match</InputLabel>
                  <Select
                    label="Request must match"
                    value={form.conditionMatchMode}
                    onChange={(e) => setForm({ ...form, conditionMatchMode: e.target.value as "Any" | "All" })}
                  >
                    <MenuItem value="All">All conditions (AND)</MenuItem>
                    <MenuItem value="Any">Any condition (OR)</MenuItem>
                  </Select>
                </FormControl>
                <Stack spacing={1.25}>
                  {conditionKinds.map((kind, index) => (
                    <Grid key={`${kind}-${index}`} container spacing={1} alignItems="center">
                      <Grid size={{ xs: 12, sm: 4 }}>
                        <FormControl fullWidth size="small">
                          <InputLabel>Condition</InputLabel>
                          <Select label="Condition" value={kind} onChange={(e) => replaceCondition(index, e.target.value as ConditionKind)}>
                            {(Object.keys(conditionLabels) as ConditionKind[]).map((value) => (
                              <MenuItem key={value} value={value} disabled={conditionKinds.includes(value) && value !== kind}>{conditionLabels[value]}</MenuItem>
                            ))}
                          </Select>
                        </FormControl>
                      </Grid>
                      <Grid size={{ xs: 12, sm: 3 }}>
                        {kind === "duration" ? (
                          <FormControl fullWidth size="small"><InputLabel>Comparison</InputLabel><Select label="Comparison" value={form.hireDurationOperator ?? "GreaterThan"} onChange={(e) => setForm({ ...form, hireDurationOperator: e.target.value })}>{options.durationOperators.map((x) => <MenuItem key={x.value} value={x.value}>{x.label}</MenuItem>)}</Select></FormControl>
                        ) : kind === "value" ? (
                          <TextField fullWidth size="small" label="Comparison" value="At least" disabled />
                        ) : (
                          <TextField fullWidth size="small" label="Comparison" value="Is" disabled />
                        )}
                      </Grid>
                      <Grid size={{ xs: 10, sm: 4 }}>
                        {kind === "assetType" ? (
                          <FormControl fullWidth size="small"><InputLabel>Value</InputLabel><Select label="Value" value={form.assetTypeCondition ?? ""} onChange={(e) => setForm({ ...form, assetTypeCondition: e.target.value })}>{options.assetTypes.map((x) => <MenuItem key={x.value} value={x.value}>{x.label}</MenuItem>)}</Select></FormControl>
                        ) : kind === "duration" ? (
                          <TextField fullWidth size="small" type="number" label="Days" inputProps={{ min: 0.01, max: 3650, step: 0.25 }} value={form.hireDurationDays ?? ""} onChange={(e) => setForm({ ...form, hireDurationDays: e.target.value === "" ? null : Number(e.target.value) })} />
                        ) : kind === "value" ? (
                          <TextField fullWidth size="small" type="number" label="FJD" inputProps={{ min: 0, step: 1 }} value={form.minimumAmount ?? ""} onChange={(e) => setForm({ ...form, minimumAmount: e.target.value === "" ? null : Number(e.target.value) })} />
                        ) : (
                          <TextField fullWidth size="small" label="Value" value="Yes" disabled />
                        )}
                      </Grid>
                      <Grid size={{ xs: 2, sm: 1 }}>
                        <Tooltip title="Remove condition"><IconButton color="error" aria-label={`Remove ${conditionLabels[kind]} condition`} onClick={() => removeCondition(index)}><DeleteOutline /></IconButton></Tooltip>
                      </Grid>
                    </Grid>
                  ))}
                </Stack>
                <Button size="small" startIcon={<AddOutlined />} onClick={addCondition} disabled={conditionKinds.length === Object.keys(conditionLabels).length} sx={{ mt: 1.5 }}>Add condition</Button>
                <Box
                  sx={{
                    mt: 1.5,
                    p: 1.25,
                    borderRadius: 1.5,
                    bgcolor: "grey.100",
                  }}
                >
                  <Typography variant="caption" color="text.secondary">
                    RULE LOGIC
                  </Typography>
                  <Typography variant="body2" fontWeight={750}>
                    {triggerLabel(form, false)}
                  </Typography>
                </Box>
              </Box>
              <Box mt={2}>
                <Stack
                  direction="row"
                  justifyContent="space-between"
                  alignItems="center"
                >
                  <Stack direction="row" alignItems="center" gap={1}>
                    <Chip label="3" color="secondary" size="small" />
                    <Typography fontWeight={800}>Who approves it?</Typography>
                  </Stack>
                  <Button
                    size="small"
                    startIcon={<AddOutlined />}
                    onClick={addStage}
                    disabled={form.stages.length >= 5}
                  >
                    Add stage
                  </Button>
                </Stack>
                <Typography variant="body2" color="text.secondary">
                  The rental officer completes the first review when submitting
                  the request. The manager assigned to the same branch and
                  division makes the final decision.
                </Typography>
              </Box>
              <Stack spacing={1.5} my={2}>
                {form.stages.map((x, i) => (
                  <Card
                    key={i}
                    variant="outlined"
                    sx={{
                      bgcolor: i === 0 ? "grey.50" : "rgba(255,232,0,.06)",
                    }}
                  >
                    <CardContent>
                      <Grid container spacing={1.5} alignItems="center">
                        <Grid size={{ xs: 12, sm: 1 }}>
                          <Chip
                            label={i + 1}
                            color={i === 0 ? "default" : "secondary"}
                          />
                        </Grid>
                        <Grid size={{ xs: 12, sm: 4 }}>
                          <TextField
                            fullWidth
                            size="small"
                            label="Stage name"
                            value={x.name}
                            onChange={(e) =>
                              updateStage(i, { name: e.target.value })
                            }
                          />
                        </Grid>
                        <Grid size={{ xs: 12, sm: 3 }}>
                          <FormControl fullWidth size="small">
                            <InputLabel>Responsible role</InputLabel>
                            <Select
                              label="Responsible role"
                              value={x.assignedRole}
                              onChange={(e) =>
                                updateStage(i, {
                                  assignedRole: e.target.value,
                                  escalationRole: "BranchManager",
                                })
                              }
                            >
                              <MenuItem value="RentalOfficer">
                                Rental officer
                              </MenuItem>
                              <MenuItem value="BranchManager">
                                Branch manager
                              </MenuItem>
                            </Select>
                          </FormControl>
                        </Grid>
                        <Grid size={{ xs: 12, sm: 3 }}>
                          <TextField
                            fullWidth
                            size="small"
                            type="number"
                            label="Due within hours"
                            value={x.escalateAfterHours}
                            onChange={(e) =>
                              updateStage(i, {
                                escalateAfterHours: Number(e.target.value),
                              })
                            }
                          />
                        </Grid>
                        <Grid size={{ xs: 12, sm: 1 }}>
                          <Tooltip title="Delete stage">
                            <IconButton
                              color="error"
                              aria-label="Delete stage"
                              onClick={() => removeStage(i)}
                            >
                              <DeleteOutline />
                            </IconButton>
                          </Tooltip>
                        </Grid>
                      </Grid>
                    </CardContent>
                  </Card>
                ))}
              </Stack>
              {form.stages.length === 0 && (
                <Alert severity="warning" sx={{ mb: 2 }}>
                  Add at least one approval stage.
                </Alert>
              )}
              <Box
                component="details"
                sx={{ mb: 2, p: 1.5, border: "1px solid", borderColor: "divider", borderRadius: 2 }}
              >
                <Typography component="summary" fontWeight={800} sx={{ cursor: "pointer" }}>
                  Advanced settings · priority
                </Typography>
                <TextField
                  fullWidth
                  type="number"
                  label="Priority"
                  helperText="Higher priority wins when equally specific rules match."
                  inputProps={{ min: 0, max: 1000 }}
                  value={form.priority}
                  onChange={(e) => setForm({ ...form, priority: Number(e.target.value) })}
                  sx={{ mt: 2 }}
                />
              </Box>
              <Stack
                direction="row"
                justifyContent="space-between"
                alignItems="center"
              >
                <FormControlLabel
                  control={
                    <Switch
                      checked={form.isActive}
                      onChange={(e) =>
                        setForm({ ...form, isActive: e.target.checked })
                      }
                    />
                  }
                  label="Rule active"
                />
                <Stack direction="row" gap={1}>
                  <Button onClick={reset}>Cancel</Button>
                  <Button
                    variant="contained"
                    disabled={saving}
                    onClick={() => void save()}
                  >
                    {saving ? "Saving…" : "Save rule"}
                  </Button>
                </Stack>
              </Stack>
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </Box>
  );
}
