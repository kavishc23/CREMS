import { createTheme } from '@mui/material/styles'

// Brand tokens: Carpenters Fiji ink + signature yellow, used sparingly as a precision accent
// rather than washed across every surface, so the product reads as a serious ops system.
export const brand = {
  ink: '#15140f',
  inkSoft: '#242320',
  yellow: '#ffed00',
  yellowDeep: '#c9ab00',
  paper: '#ffffff',
  canvas: '#f2f2ed',
  line: '#e6e3d9',
  lineStrong: '#d8d4c4',
  textPrimary: '#1c1b17',
  textSecondary: '#6b675c',
}

// Staff workspaces share one visual language, including their portal dialogs.
export const staffTheme = createTheme({
  palette: {
    primary: { main: brand.ink, dark: '#000000', light: brand.inkSoft, contrastText: '#fff' },
    secondary: { main: brand.yellow, dark: brand.yellowDeep, contrastText: brand.ink },
    background: { default: brand.canvas, paper: brand.paper },
    text: { primary: brand.textPrimary, secondary: brand.textSecondary },
    divider: brand.line,
    success: { main: '#1f7a52' }, warning: { main: '#96660a' },
    error: { main: '#b13a3a' }, info: { main: '#3d647e' },
  },
  shape: { borderRadius: 6 },
  typography: {
    fontFamily: 'Inter, sans-serif', fontSize: 13,
    h4: { fontSize: 'clamp(1.55rem, 2vw, 1.9rem)', fontWeight: 800, letterSpacing: '-.035em', lineHeight: 1.15 },
    h5: { fontSize: '1.25rem', fontWeight: 750, letterSpacing: '-.02em', lineHeight: 1.25 },
    h6: { fontSize: '1.02rem', fontWeight: 700, letterSpacing: '-.01em' },
    body1: { fontSize: '.875rem', lineHeight: 1.65 },
    body2: { fontSize: '.8125rem', lineHeight: 1.55 },
    overline: { fontSize: '.66rem', fontWeight: 750, letterSpacing: '.12em' },
    button: { textTransform: 'none', fontWeight: 650 },
  },
  components: {
    MuiCssBaseline: { styleOverrides: {
      '.crems-scroll': { scrollbarWidth: 'thin', scrollbarColor: `${brand.lineStrong} transparent` },
      '.crems-scroll::-webkit-scrollbar': { width: 6, height: 6 },
      '.crems-scroll::-webkit-scrollbar-thumb': { backgroundColor: brand.lineStrong, borderRadius: 8 },
    } },
    MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: {
      root: { borderRadius: 4, minHeight: 36, padding: '8px 16px', flexShrink: 0, height: 'fit-content', lineHeight: 1.35 },
      outlined: { borderColor: brand.lineStrong, '&:hover': { borderColor: brand.ink, backgroundColor: 'rgba(21,20,15,.04)' } },
      containedPrimary: { '&:hover': { backgroundColor: brand.inkSoft } },
      containedSecondary: { color: brand.ink, fontWeight: 750, '&:hover': { backgroundColor: brand.yellowDeep } },
      sizeSmall: { minHeight: 30, padding: '5px 12px' },
    } },
    MuiIconButton: { styleOverrides: { root: { borderRadius: 5, '&.Mui-focusVisible': { outline: `2px solid ${brand.yellowDeep}`, outlineOffset: 2 } } } },
    MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } },
    MuiCard: { defaultProps: { elevation: 0 }, styleOverrides: { root: { border: `1px solid ${brand.line}`, boxShadow: '0 5px 18px rgba(20,18,10,.04)', borderRadius: 6 } } },
    MuiCardContent: { styleOverrides: { root: { padding: 20, '&:last-child': { paddingBottom: 20 } } } },
    MuiTabs: { styleOverrides: { root: { minHeight: 46, backgroundColor: 'transparent' }, indicator: { height: 2.5, backgroundColor: brand.ink, borderRadius: '3px 3px 0 0' } } },
    MuiTab: { styleOverrides: { root: { minHeight: 46, textTransform: 'none', fontWeight: 650, fontSize: '.8rem', padding: '12px 16px', color: brand.textSecondary, '&.Mui-selected': { color: brand.ink, fontWeight: 750 } } } },
    MuiTableContainer: { styleOverrides: { root: { overflowX: 'auto' } } },
    MuiTableHead: { styleOverrides: { root: { backgroundColor: '#f7f6f1', '& .MuiTableRow-root': { backgroundColor: '#f7f6f1' } } } },
    MuiTableCell: { styleOverrides: {
      root: { borderBottom: `1px solid ${brand.line}`, padding: '14px 18px' },
      head: { fontSize: '.68rem', fontWeight: 750, color: '#5b5748', textTransform: 'uppercase', whiteSpace: 'nowrap', letterSpacing: '.045em', padding: '12px 18px', borderBottom: `1px solid ${brand.lineStrong}` },
      body: { fontSize: '.8rem', fontVariantNumeric: 'tabular-nums' },
    } },
    MuiTableRow: { styleOverrides: { root: { '&.MuiTableRow-hover:hover': { backgroundColor: '#faf9f3' }, '&.Mui-selected, &.Mui-selected:hover': { backgroundColor: '#fdf9dd' }, '&:last-child td': { borderBottom: 0 } } } },
    MuiChip: { defaultProps: { size: 'small' }, styleOverrides: { root: { fontWeight: 700, fontSize: '.68rem', borderRadius: 3, letterSpacing: '.01em' }, colorDefault: { backgroundColor: '#ecebe4', color: '#514e44' }, outlined: { backgroundColor: 'transparent' } } },
    MuiTextField: { defaultProps: { size: 'small' } },
    MuiFormControl: { defaultProps: { size: 'small' } },
    MuiOutlinedInput: { styleOverrides: { root: { backgroundColor: '#fff', borderRadius: 4, fontSize: '.85rem', '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: brand.ink, borderWidth: 1.5 } }, notchedOutline: { borderColor: brand.lineStrong } } },
    MuiAlert: { styleOverrides: { root: { borderRadius: 7, fontSize: '.8rem', padding: '8px 14px' }, standardInfo: { backgroundColor: '#eaf1f4', color: '#324e5c' }, standardSuccess: { backgroundColor: '#eaf5ee' }, standardWarning: { backgroundColor: '#fbf3df' }, standardError: { backgroundColor: '#fbeeec' } } },
    MuiDialog: { styleOverrides: { paper: { borderRadius: 14, maxHeight: 'calc(100dvh - 32px)', boxShadow: '0 28px 90px rgba(21,20,15,.22)' }, paperFullScreen: { borderRadius: 0, maxHeight: '100dvh' } } },
    MuiDialogTitle: { styleOverrides: { root: { fontSize: '1.15rem', fontWeight: 750, padding: '22px 24px', backgroundColor: '#fcfbf7', borderBottom: `1px solid ${brand.line}` } } },
    MuiDialogContent: { styleOverrides: { root: { padding: 24, overflowY: 'auto', '&.MuiDialogContent-root': { paddingTop: 24 } } } },
    MuiDialogActions: { styleOverrides: { root: { padding: '16px 24px', borderTop: `1px solid ${brand.line}`, backgroundColor: '#fcfbf8', gap: 8 } } },
    MuiAccordion: { defaultProps: { elevation: 0 }, styleOverrides: { root: { border: `1px solid ${brand.line}`, '&:before': { display: 'none' }, '&.Mui-expanded': { margin: '12px 0' } } } },
    MuiStepIcon: { styleOverrides: { root: { '&.Mui-active': { color: brand.yellowDeep }, '&.Mui-completed': { color: '#1f7a52' } } } },
    MuiTooltip: { defaultProps: { arrow: true }, styleOverrides: { tooltip: { backgroundColor: brand.ink, fontSize: '.75rem' } } },
    MuiLinearProgress: { styleOverrides: { root: { borderRadius: 8, height: 6, backgroundColor: '#eeece3' }, bar: { borderRadius: 8, backgroundColor: brand.ink } } },
    MuiDivider: { styleOverrides: { root: { borderColor: brand.line } } },
  },
})
