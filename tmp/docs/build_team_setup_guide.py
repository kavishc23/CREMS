from pathlib import Path
from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT, WD_TABLE_ALIGNMENT
from docx.enum.section import WD_SECTION
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT = Path('/Users/kavishchandra/Documents/CS400')
OUT = ROOT / 'docs' / 'CREMS-Team-Setup-Guide.docx'
LOGO = ROOT / 'src' / 'crems-web' / 'public' / 'brand' / 'carpenters-logo.png'

YELLOW = 'FFED00'
BLACK = '111111'
DARK_GRAY = '454545'
MUTED = '6A6A6A'
LIGHT = 'F5F5F1'
BORDER = 'D9D9D3'
WHITE = 'FFFFFF'
BLUE = '1F4D78'

doc = Document()
section = doc.sections[0]
section.page_width = Inches(8.5)
section.page_height = Inches(11)
section.top_margin = Inches(0.85)
section.bottom_margin = Inches(0.8)
section.left_margin = Inches(1.0)
section.right_margin = Inches(1.0)
section.header_distance = Inches(0.35)
section.footer_distance = Inches(0.4)

styles = doc.styles

def set_style_font(style, name, size, color=BLACK, bold=None):
    style.font.name = name
    style._element.rPr.rFonts.set(qn('w:ascii'), name)
    style._element.rPr.rFonts.set(qn('w:hAnsi'), name)
    style.font.size = Pt(size)
    style.font.color.rgb = RGBColor.from_string(color)
    if bold is not None:
        style.font.bold = bold

normal = styles['Normal']
set_style_font(normal, 'Calibri', 11, BLACK)
normal.paragraph_format.space_before = Pt(0)
normal.paragraph_format.space_after = Pt(6)
normal.paragraph_format.line_spacing = 1.25

for name, size, color, before, after in [
    ('Title', 28, BLACK, 0, 8),
    ('Subtitle', 14, DARK_GRAY, 0, 12),
    ('Heading 1', 16, BLACK, 18, 10),
    ('Heading 2', 13, BLACK, 14, 7),
    ('Heading 3', 12, BLUE, 10, 5),
]:
    style = styles[name]
    set_style_font(style, 'Calibri', size, color, bold=(name != 'Subtitle'))
    style.paragraph_format.space_before = Pt(before)
    style.paragraph_format.space_after = Pt(after)
    style.paragraph_format.keep_with_next = True

for name in ['List Bullet', 'List Number']:
    style = styles[name]
    set_style_font(style, 'Calibri', 11, BLACK)
    style.paragraph_format.left_indent = Inches(0.375)
    style.paragraph_format.first_line_indent = Inches(-0.188)
    style.paragraph_format.space_after = Pt(4)
    style.paragraph_format.line_spacing = 1.25

def shade_paragraph(p, fill):
    pPr = p._p.get_or_add_pPr()
    shd = pPr.find(qn('w:shd'))
    if shd is None:
        shd = OxmlElement('w:shd')
        pPr.append(shd)
    shd.set(qn('w:fill'), fill)

def set_paragraph_border(p, color=YELLOW, size='18', side='left'):
    pPr = p._p.get_or_add_pPr()
    pBdr = pPr.find(qn('w:pBdr'))
    if pBdr is None:
        pBdr = OxmlElement('w:pBdr')
        pPr.append(pBdr)
    border = OxmlElement(f'w:{side}')
    border.set(qn('w:val'), 'single')
    border.set(qn('w:sz'), size)
    border.set(qn('w:space'), '8')
    border.set(qn('w:color'), color)
    pBdr.append(border)

def set_cell_shading(cell, fill):
    tcPr = cell._tc.get_or_add_tcPr()
    shd = tcPr.find(qn('w:shd'))
    if shd is None:
        shd = OxmlElement('w:shd')
        tcPr.append(shd)
    shd.set(qn('w:fill'), fill)

def set_cell_margins(cell, top=100, start=120, bottom=100, end=120):
    tc = cell._tc
    tcPr = tc.get_or_add_tcPr()
    tcMar = tcPr.first_child_found_in('w:tcMar')
    if tcMar is None:
        tcMar = OxmlElement('w:tcMar')
        tcPr.append(tcMar)
    for m, value in [('top', top), ('start', start), ('bottom', bottom), ('end', end)]:
        node = tcMar.find(qn(f'w:{m}'))
        if node is None:
            node = OxmlElement(f'w:{m}')
            tcMar.append(node)
        node.set(qn('w:w'), str(value))
        node.set(qn('w:type'), 'dxa')

def set_table_geometry(table, widths_dxa, indent=120):
    table.autofit = False
    tbl = table._tbl
    tblPr = tbl.tblPr
    tblW = tblPr.find(qn('w:tblW'))
    if tblW is None:
        tblW = OxmlElement('w:tblW')
        tblPr.append(tblW)
    tblW.set(qn('w:w'), str(sum(widths_dxa)))
    tblW.set(qn('w:type'), 'dxa')
    tblInd = tblPr.find(qn('w:tblInd'))
    if tblInd is None:
        tblInd = OxmlElement('w:tblInd')
        tblPr.append(tblInd)
    tblInd.set(qn('w:w'), str(indent))
    tblInd.set(qn('w:type'), 'dxa')
    grid = tbl.tblGrid
    for child in list(grid):
        grid.remove(child)
    for width in widths_dxa:
        col = OxmlElement('w:gridCol')
        col.set(qn('w:w'), str(width))
        grid.append(col)
    for row in table.rows:
        for idx, cell in enumerate(row.cells):
            tcPr = cell._tc.get_or_add_tcPr()
            tcW = tcPr.find(qn('w:tcW'))
            if tcW is None:
                tcW = OxmlElement('w:tcW')
                tcPr.append(tcW)
            tcW.set(qn('w:w'), str(widths_dxa[idx]))
            tcW.set(qn('w:type'), 'dxa')
            set_cell_margins(cell)
            cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER

def add_page_field(paragraph):
    run = paragraph.add_run()
    fldChar1 = OxmlElement('w:fldChar')
    fldChar1.set(qn('w:fldCharType'), 'begin')
    instrText = OxmlElement('w:instrText')
    instrText.set(qn('xml:space'), 'preserve')
    instrText.text = ' PAGE '
    fldChar2 = OxmlElement('w:fldChar')
    fldChar2.set(qn('w:fldCharType'), 'end')
    run._r.extend([fldChar1, instrText, fldChar2])

def configure_header_footer(sec):
    header = sec.header
    hp = header.paragraphs[0]
    hp.clear()
    hp.alignment = WD_ALIGN_PARAGRAPH.LEFT
    if LOGO.exists():
        hp.add_run().add_picture(str(LOGO), width=Inches(0.28))
    r = hp.add_run('   CREMS Team Setup Guide')
    r.bold = True
    r.font.name = 'Calibri'
    r.font.size = Pt(9)
    r.font.color.rgb = RGBColor.from_string(DARK_GRAY)
    footer = sec.footer
    fp = footer.paragraphs[0]
    fp.alignment = WD_ALIGN_PARAGRAPH.RIGHT
    r = fp.add_run('Car and Rental Equipment Management System  |  Page ')
    r.font.name = 'Calibri'
    r.font.size = Pt(8.5)
    r.font.color.rgb = RGBColor.from_string(MUTED)
    add_page_field(fp)

configure_header_footer(section)

def add_heading(text, level=1):
    p = doc.add_heading(text, level=level)
    if level == 1:
        set_paragraph_border(p, YELLOW, '22', 'left')
    return p

def add_body(text, bold_prefix=None):
    p = doc.add_paragraph()
    if bold_prefix and text.startswith(bold_prefix):
        p.add_run(bold_prefix).bold = True
        p.add_run(text[len(bold_prefix):])
    else:
        p.add_run(text)
    return p

def add_bullet(text):
    return doc.add_paragraph(text, style='List Bullet')

def add_number(text):
    return doc.add_paragraph(text, style='List Number')

def add_code(lines, label=None):
    if label:
        p = doc.add_paragraph()
        p.paragraph_format.space_after = Pt(3)
        r = p.add_run(label)
        r.bold = True
        r.font.color.rgb = RGBColor.from_string(DARK_GRAY)
    p = doc.add_paragraph()
    p.paragraph_format.left_indent = Inches(0.16)
    p.paragraph_format.right_indent = Inches(0.16)
    p.paragraph_format.space_before = Pt(2)
    p.paragraph_format.space_after = Pt(8)
    p.paragraph_format.line_spacing = 1.05
    p.paragraph_format.keep_together = True
    shade_paragraph(p, 'F1F1ED')
    set_paragraph_border(p, YELLOW, '16', 'left')
    for i, line in enumerate(lines):
        if i:
            p.add_run('\n')
        r = p.add_run(line)
        r.font.name = 'Courier New'
        r._element.rPr.rFonts.set(qn('w:ascii'), 'Courier New')
        r._element.rPr.rFonts.set(qn('w:hAnsi'), 'Courier New')
        r.font.size = Pt(9)
        r.font.color.rgb = RGBColor.from_string(BLACK)
    return p

def add_note(title, text, kind='note'):
    p = doc.add_paragraph()
    p.paragraph_format.left_indent = Inches(0.12)
    p.paragraph_format.right_indent = Inches(0.12)
    p.paragraph_format.space_before = Pt(5)
    p.paragraph_format.space_after = Pt(8)
    shade_paragraph(p, 'FFF9C7' if kind == 'warning' else LIGHT)
    set_paragraph_border(p, YELLOW if kind == 'warning' else BLACK, '18', 'left')
    r = p.add_run(f'{title}: ')
    r.bold = True
    p.add_run(text)
    return p

def add_checklist(items):
    for item in items:
        doc.add_paragraph(item, style='List Bullet')

def repeat_table_header(row):
    tr_pr = row._tr.get_or_add_trPr()
    tbl_header = OxmlElement('w:tblHeader')
    tbl_header.set(qn('w:val'), 'true')
    tr_pr.append(tbl_header)

# Cover
doc.add_paragraph().paragraph_format.space_after = Pt(34)
if LOGO.exists():
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.add_run().add_picture(str(LOGO), width=Inches(1.35))

p = doc.add_paragraph()
p.alignment = WD_ALIGN_PARAGRAPH.CENTER
r = p.add_run('CREMS Team Setup Guide')
r.bold = True
r.font.size = Pt(28)
r.font.color.rgb = RGBColor.from_string(BLACK)
p = doc.add_paragraph(style='Subtitle')
p.alignment = WD_ALIGN_PARAGRAPH.CENTER
p.add_run('Run the complete frontend, backend and SQL Server environment')

p = doc.add_paragraph()
p.alignment = WD_ALIGN_PARAGRAPH.CENTER
p.paragraph_format.space_before = Pt(8)
p.paragraph_format.space_after = Pt(24)
r = p.add_run('macOS + Windows  |  React + ASP.NET Core + SQL Server')
r.bold = True
r.font.size = Pt(11)
r.font.color.rgb = RGBColor.from_string(DARK_GRAY)

callout = doc.add_paragraph()
callout.alignment = WD_ALIGN_PARAGRAPH.CENTER
callout.paragraph_format.left_indent = Inches(0.6)
callout.paragraph_format.right_indent = Inches(0.6)
callout.paragraph_format.space_before = Pt(18)
callout.paragraph_format.space_after = Pt(18)
shade_paragraph(callout, YELLOW)
r = callout.add_run('Outcome')
r.bold = True
r.font.size = Pt(12)
callout.add_run('\nEach team member will run an independent local CREMS environment with a private administrator account and database.')

p = doc.add_paragraph()
p.alignment = WD_ALIGN_PARAGRAPH.CENTER
p.paragraph_format.space_before = Pt(36)
r = p.add_run('Repository')
r.bold = True
r.font.color.rgb = RGBColor.from_string(MUTED)
p.add_run('\nhttps://github.com/kavishc23/CREMS')

p = doc.add_paragraph()
p.alignment = WD_ALIGN_PARAGRAPH.CENTER
p.paragraph_format.space_before = Pt(20)
r = p.add_run('CS400 Industry Experience Project')
r.font.size = Pt(10)
r.font.color.rgb = RGBColor.from_string(MUTED)

doc.add_page_break()

# Overview
add_heading('1. How the local system works')
add_body('Every team member runs the same three parts on their own computer. This avoids sharing database passwords or depending on one person’s laptop during development.')

table = doc.add_table(rows=1, cols=3)
table.alignment = WD_TABLE_ALIGNMENT.CENTER
table.style = 'Table Grid'
headers = ['Part', 'Technology', 'Local address']
repeat_table_header(table.rows[0])
for i, text in enumerate(headers):
    cell = table.rows[0].cells[i]
    set_cell_shading(cell, YELLOW)
    run = cell.paragraphs[0].add_run(text)
    run.bold = True
for row_data in [
    ('Frontend', 'React + TypeScript + Vite', 'http://localhost:5173'),
    ('Backend API', 'ASP.NET Core 10', 'http://localhost:5080'),
    ('Database', 'SQL Server 2022 in Docker', 'localhost:1433'),
]:
    cells = table.add_row().cells
    for i, text in enumerate(row_data):
        cells[i].text = text
set_table_geometry(table, [1900, 4000, 3460])

add_note('Important', 'Do not copy another member’s .env file, administrator password, user-secrets store, node_modules folder, .dotnet folder or Docker data volume.', 'warning')

add_heading('2. Accounts and access required')
add_checklist([
    'A GitHub account that has accepted the private CREMS repository invitation.',
    'Permission to clone https://github.com/kavishc23/CREMS.',
    'Administrator rights on the computer for installing developer tools.',
    'A personal local CREMS administrator password that is not shared in chat or committed to Git.',
])

add_heading('3. Software prerequisites')
table = doc.add_table(rows=1, cols=4)
table.style = 'Table Grid'
table.alignment = WD_TABLE_ALIGNMENT.CENTER
repeat_table_header(table.rows[0])
for i, text in enumerate(['Software', 'Required version', 'macOS', 'Windows']):
    cell = table.rows[0].cells[i]
    set_cell_shading(cell, YELLOW)
    run = cell.paragraphs[0].add_run(text)
    run.bold = True
rows = [
    ('Git', 'Current stable', 'Xcode tools or Git installer', 'Git for Windows'),
    ('.NET SDK', '10.x', 'Arm64 for Apple Silicon', 'x64 for most PCs'),
    ('Node.js', '22 LTS or newer', 'Arm64 or x64', 'x64 for most PCs'),
    ('Docker Desktop', 'Current stable', 'Apple/Intel build as appropriate', 'WSL 2 backend; Linux containers'),
    ('VS Code', 'Current stable', 'C# Dev Kit + ESLint', 'C# Dev Kit + ESLint'),
]
for row_data in rows:
    cells = table.add_row().cells
    for i, text in enumerate(row_data):
        cells[i].text = text
set_table_geometry(table, [1600, 1600, 2980, 3180])

add_note('Installation sources', 'Download developer tools only from their official websites: git-scm.com, dotnet.microsoft.com, nodejs.org, docker.com and code.visualstudio.com.')

# Mac
add_heading('4. macOS setup')
add_heading('4.1 Verify prerequisites', 2)
add_code([
    'git --version',
    'dotnet --version',
    'node --version',
    'npm --version',
    'docker --version',
    'docker compose version',
], 'Run in Terminal or the VS Code terminal:')
add_body('Expected: .NET reports a 10.x SDK, Node.js reports version 22 or newer, and Docker reports both Engine and Compose versions.')

add_heading('4.2 Clone the repository', 2)
add_code([
    'cd ~/Documents',
    'git clone https://github.com/kavishc23/CREMS.git',
    'cd CREMS',
])
add_note('Authentication', 'If Git asks for a password, use GitHub CLI (gh auth login), GitHub Desktop, SSH, or a personal access token. GitHub account passwords do not work for Git pushes.')

add_heading('4.3 Restore project dependencies', 2)
add_code([
    'dotnet tool restore',
    'dotnet restore CREMS.slnx',
    'cd src/crems-web',
    'npm install',
    'cd ../..',
])

add_heading('4.4 Start Docker and SQL Server', 2)
add_number('Open Docker Desktop and wait until the Docker engine reports that it is running.')
add_number('From the CREMS repository root, start SQL Server:')
add_code(['docker compose up -d'])
add_number('Confirm the container is healthy:')
add_code(['docker compose ps'])
add_body('Expected status: cs400-sqlserver-1 is Up and healthy. The first run downloads a large SQL Server image and may take several minutes.')

add_heading('4.5 Configure the local administrator', 2)
add_code([
    'dotnet user-secrets set "BootstrapAdmin:Email" "YOUR_EMAIL" --project src/CREMS.Api',
    'dotnet user-secrets set "BootstrapAdmin:Password" "YOUR_PRIVATE_PASSWORD" --project src/CREMS.Api',
    'dotnet user-secrets set "BootstrapAdmin:FullName" "YOUR_NAME" --project src/CREMS.Api',
])
add_note('Password rules', 'Use at least 10 characters with uppercase, lowercase and a number. A symbol is strongly recommended. Do not type the words YOUR_PRIVATE_PASSWORD literally.', 'warning')

# Windows
add_heading('5. Windows setup')
add_heading('5.1 Docker Desktop preparation', 2)
add_bullet('Install Docker Desktop with the WSL 2 backend enabled.')
add_bullet('Restart Windows if the installer requests it.')
add_bullet('Open Docker Desktop and verify that it is using Linux containers.')
add_bullet('Wait until Docker Desktop reports that the engine is running.')

add_heading('5.2 Verify prerequisites in PowerShell', 2)
add_code([
    'git --version',
    'dotnet --version',
    'node --version',
    'npm --version',
    'docker --version',
    'docker compose version',
], 'Open PowerShell or the VS Code PowerShell terminal:')
add_note('PATH troubleshooting', 'If a newly installed command is not found, close and reopen VS Code. If it still fails, restart Windows.')

add_heading('5.3 Clone the repository', 2)
add_code([
    'cd $HOME\\Documents',
    'git clone https://github.com/kavishc23/CREMS.git',
    'cd CREMS',
])

add_heading('5.4 Restore project dependencies', 2)
add_code([
    'dotnet tool restore',
    'dotnet restore CREMS.slnx',
    'cd src\\crems-web',
    'npm install',
    'cd ..\\..',
])

add_heading('5.5 Start SQL Server', 2)
add_code([
    'docker compose up -d',
    'docker compose ps',
])
add_body('Expected status: cs400-sqlserver-1 is Up and healthy. Docker may run the SQL Server image through emulation depending on the computer architecture.')

add_heading('5.6 Configure the local administrator', 2)
add_code([
    'dotnet user-secrets set "BootstrapAdmin:Email" "YOUR_EMAIL" --project src/CREMS.Api',
    'dotnet user-secrets set "BootstrapAdmin:Password" "YOUR_PRIVATE_PASSWORD" --project src/CREMS.Api',
    'dotnet user-secrets set "BootstrapAdmin:FullName" "YOUR_NAME" --project src/CREMS.Api',
])

# Running
add_heading('6. Run the complete project')
add_body('Use two terminals and leave both processes running while developing.')

add_heading('Terminal 1 — backend API', 2)
add_code([
    'cd PATH_TO_CREMS',
    'dotnet run --project src/CREMS.Api',
])
add_body('Successful startup includes: Now listening on: http://localhost:5080')

add_heading('Terminal 2 — frontend', 2)
add_code([
    'cd PATH_TO_CREMS/src/crems-web       # macOS',
    'cd PATH_TO_CREMS\\src\\crems-web     # Windows PowerShell',
    'npm run dev',
])
add_body('Open http://localhost:5173 and sign in using the email and password stored in your local user-secrets store.')

add_note('Keep services running', 'Do not close either terminal while using CREMS. Press Ctrl+C in each terminal when you want to stop the backend or frontend.')

add_heading('7. First-run verification checklist')
add_checklist([
    'Docker Desktop is running.',
    'docker compose ps shows SQL Server as healthy.',
    'The backend terminal shows http://localhost:5080.',
    'Opening http://localhost:5080/api/health returns a healthy response.',
    'The frontend terminal shows http://localhost:5173.',
    'The Carpenters-branded login page loads.',
    'The local administrator can sign in.',
    'The dashboard displays after authentication.',
    'The sidebar expands, collapses and opens correctly on smaller screens.',
])

add_heading('8. Daily start and stop routine')
add_heading('Start work', 2)
add_code([
    'git switch main',
    'git pull',
    'docker compose up -d',
    'dotnet run --project src/CREMS.Api',
    '# In a second terminal:',
    'cd src/crems-web',
    'npm run dev',
])

add_heading('Stop work', 2)
add_body('Press Ctrl+C in the backend and frontend terminals. SQL Server can remain running. To stop it and preserve the database volume:')
add_code(['docker compose stop'])
add_note('Do not use docker compose down -v', 'The -v option deletes the local SQL Server data volume, including the local CREMS database.', 'warning')

add_heading('9. Working safely with Git')
add_body('Never develop directly on main. Create a feature branch for each task and open a pull request for team review.')
add_code([
    'git switch main',
    'git pull',
    'git switch -c feature/short-feature-name',
    '# Make and test changes',
    'git add .',
    'git commit -m "Describe the completed change"',
    'git push -u origin feature/short-feature-name',
])
add_bullet('Do not commit real passwords, .env files, user secrets or customer data.')
add_bullet('Pull main before creating a branch.')
add_bullet('Use clear commit messages and small, reviewable changes.')
add_bullet('Ask another member to review the pull request before merging.')

# Troubleshooting
add_heading('10. Troubleshooting')
table = doc.add_table(rows=1, cols=2)
table.style = 'Table Grid'
table.alignment = WD_TABLE_ALIGNMENT.CENTER
repeat_table_header(table.rows[0])
for i, text in enumerate(['Problem', 'Resolution']):
    cell = table.rows[0].cells[i]
    set_cell_shading(cell, YELLOW)
    run = cell.paragraphs[0].add_run(text)
    run.bold = True
issues = [
    ('dotnet is not recognized / command not found', 'Install the .NET 10 SDK, then restart VS Code. Confirm with dotnet --version.'),
    ('C# Dev Kit reports no SDK', 'Confirm .NET 10 is installed globally and reload the VS Code window.'),
    ('Docker command cannot connect', 'Open Docker Desktop and wait until the engine is running.'),
    ('SQL Server remains unhealthy', 'Run docker compose logs sqlserver. Confirm the Compose file is current and port 1433 is available.'),
    ('Port 1433 is already in use', 'Stop the other SQL Server/container using that port, then run docker compose up -d again.'),
    ('Administrator password rejected', 'Use 10+ characters with uppercase, lowercase and at least one number.'),
    ('Login page appears but login fails', 'Confirm the backend is running, the administrator email is correct, and user secrets were set for src/CREMS.Api.'),
    ('Frontend stays on a loading indicator', 'Confirm the backend is running. Wait up to eight seconds, then refresh the page.'),
    ('npm install fails', 'Confirm internet access and Node.js 22+, then remove no project files; rerun npm install.'),
    ('GitHub rejects password authentication', 'Use gh auth login, GitHub Desktop, SSH or a personal access token.'),
]
for problem, resolution in issues:
    cells = table.add_row().cells
    cells[0].text = problem
    cells[1].text = resolution
set_table_geometry(table, [3200, 6160])

add_heading('11. Reset the local development environment')
add_body('Use this only when the local database can be discarded. It removes the SQL Server container and the CREMS Docker volume, then creates a clean database on the next start.')
add_code([
    'docker compose down -v',
    'docker compose up -d',
    'dotnet run --project src/CREMS.Api',
])
add_note('Data loss', 'This reset permanently deletes that team member’s local CREMS database. It does not affect GitHub or another member’s computer.', 'warning')

doc.add_page_break()
add_heading('12. Quick command reference')
table = doc.add_table(rows=1, cols=2)
table.style = 'Table Grid'
table.alignment = WD_TABLE_ALIGNMENT.CENTER
repeat_table_header(table.rows[0])
for i, text in enumerate(['Task', 'Command']):
    cell = table.rows[0].cells[i]
    set_cell_shading(cell, YELLOW)
    run = cell.paragraphs[0].add_run(text)
    run.bold = True
commands = [
    ('Update local code', 'git pull'),
    ('Restore .NET tools', 'dotnet tool restore'),
    ('Restore backend packages', 'dotnet restore CREMS.slnx'),
    ('Install frontend packages', 'npm install (inside src/crems-web)'),
    ('Start database', 'docker compose up -d'),
    ('Check database', 'docker compose ps'),
    ('Start backend', 'dotnet run --project src/CREMS.Api'),
    ('Start frontend', 'npm run dev (inside src/crems-web)'),
    ('Build backend', 'dotnet build CREMS.slnx'),
    ('Build frontend', 'npm run build (inside src/crems-web)'),
    ('Stop database', 'docker compose stop'),
]
for task, command in commands:
    cells = table.add_row().cells
    cells[0].text = task
    cells[1].text = command
    for run in cells[1].paragraphs[0].runs:
        run.font.name = 'Courier New'
        run.font.size = Pt(9)
set_table_geometry(table, [3000, 6360])

# Keep tables and headings clean
for table in doc.tables:
    for row in table.rows:
        row._tr.get_or_add_trPr()
        for cell in row.cells:
            for paragraph in cell.paragraphs:
                paragraph.paragraph_format.space_before = Pt(0)
                paragraph.paragraph_format.space_after = Pt(2)
                paragraph.paragraph_format.line_spacing = 1.05
                for run in paragraph.runs:
                    if run.font.size is None:
                        run.font.size = Pt(9.5)
                    if run.font.name is None:
                        run.font.name = 'Calibri'

# Avoid heading or code-label orphans
for p in doc.paragraphs:
    if p.style.name.startswith('Heading'):
        p.paragraph_format.keep_with_next = True

OUT.parent.mkdir(parents=True, exist_ok=True)
doc.save(OUT)
print(OUT)
