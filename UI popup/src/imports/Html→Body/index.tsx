import svgPaths from "./svg-c2m9ej26yt";

function Container1() {
  return (
    <div className="h-[18px] relative shrink-0 w-[22px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 22 18">
        <g id="Container">
          <path d={svgPaths.pb257040} fill="var(--fill-0, white)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Background() {
  return (
    <div className="bg-[#4648d4] content-stretch flex items-center justify-center relative rounded-[8px] shrink-0 size-[40px]" data-name="Background">
      <Container1 />
    </div>
  );
}

function Heading() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0 w-full" data-name="Heading 1">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:SemiBold',sans-serif] font-semibold justify-center leading-[0] relative shrink-0 text-[#4648d4] text-[18px] whitespace-nowrap">
        <p className="leading-[25.2px]">AI Study Hub</p>
      </div>
    </div>
  );
}

function Container3() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0 w-full" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center leading-[0] relative shrink-0 text-[#464554] text-[10px] tracking-[0.5px] uppercase whitespace-nowrap">
        <p className="leading-[15px]">ACADEMIC WORKSPACE</p>
      </div>
    </div>
  );
}

function Container2() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0 w-[121.98px]" data-name="Container">
      <Heading />
      <Container3 />
    </div>
  );
}

function Container() {
  return (
    <div className="relative shrink-0 w-full" data-name="Container">
      <div className="flex flex-row items-center size-full">
        <div className="content-stretch flex gap-[8px] items-center px-[8px] relative size-full">
          <Background />
          <Container2 />
        </div>
      </div>
    </div>
  );
}

function Margin() {
  return (
    <div className="relative shrink-0 w-full" data-name="Margin">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex flex-col items-start pb-[32px] relative size-full">
        <Container />
      </div>
    </div>
  );
}

function Container4() {
  return (
    <div className="h-[18px] relative shrink-0 w-[16px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 16 18">
        <g id="Container">
          <path d={svgPaths.p12a32500} fill="var(--fill-0, #464554)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Container5() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Regular',sans-serif] font-normal justify-center leading-[0] relative shrink-0 text-[#464554] text-[14px] whitespace-nowrap">
        <p className="leading-[21px]">Home</p>
      </div>
    </div>
  );
}

function Link() {
  return (
    <div className="relative rounded-[8px] shrink-0 w-full" data-name="Link">
      <div className="flex flex-row items-center size-full">
        <div className="content-stretch flex gap-[16px] items-center px-[16px] py-[8px] relative size-full">
          <Container4 />
          <Container5 />
        </div>
      </div>
    </div>
  );
}

function Container6() {
  return (
    <div className="h-[16px] relative shrink-0 w-[22px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 22 16">
        <g id="Container">
          <path d={svgPaths.p378800} fill="var(--fill-0, #5D6478)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Container7() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Regular',sans-serif] font-normal justify-center leading-[0] relative shrink-0 text-[#5d6478] text-[14px] whitespace-nowrap">
        <p className="leading-[21px]">My Notebooks</p>
      </div>
    </div>
  );
}

function Link1() {
  return (
    <div className="bg-[#dbe2fa] relative rounded-[8px] shrink-0 w-full" data-name="Link">
      <div className="flex flex-row items-center size-full">
        <div className="content-stretch flex gap-[16px] items-center px-[16px] py-[8px] relative size-full">
          <Container6 />
          <Container7 />
        </div>
      </div>
    </div>
  );
}

function Nav() {
  return (
    <div className="flex-[1_0_0] min-h-px relative w-full" data-name="Nav">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex flex-col gap-[8px] items-start relative size-full">
        <Link />
        <Link1 />
      </div>
    </div>
  );
}

function AsideBackgroundDashboardContextBlurred() {
  return (
    <div className="absolute bg-[#f2f4f6] blur-[1px] content-stretch flex flex-col h-[1024px] items-start left-0 opacity-40 pl-[16px] pr-[17px] py-[16px] top-0 w-[256px]" data-name="Aside - Background Dashboard Context (Blurred)">
      <div aria-hidden className="absolute border-[#c7c4d7] border-r border-solid inset-0 pointer-events-none" />
      <Margin />
      <Nav />
    </div>
  );
}

function Container8() {
  return (
    <div className="relative shrink-0 size-[18px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 18 18">
        <g id="Container">
          <path d={svgPaths.p8a35e00} fill="var(--fill-0, #767586)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Container9() {
  return (
    <div className="content-stretch flex flex-col items-start overflow-clip relative shrink-0 w-full" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Regular',sans-serif] font-normal justify-center leading-[0] relative shrink-0 text-[#6b7280] text-[14px] w-full">
        <p className="leading-[normal]">Search...</p>
      </div>
    </div>
  );
}

function Input() {
  return (
    <div className="relative shrink-0 w-[192px]" data-name="Input">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex flex-col items-start overflow-clip pb-[10px] pt-[9px] px-[12px] relative rounded-[inherit] size-full">
        <Container9 />
      </div>
    </div>
  );
}

function BackgroundBorder() {
  return (
    <div className="bg-[#f2f4f6] content-stretch flex gap-[16px] items-center px-[17px] py-[9px] relative rounded-[9999px] shrink-0" data-name="Background+Border">
      <div aria-hidden className="absolute border border-[rgba(199,196,215,0.3)] border-solid inset-0 pointer-events-none rounded-[9999px]" />
      <Container8 />
      <Input />
    </div>
  );
}

function Header() {
  return (
    <div className="bg-[#f7f9fb] drop-shadow-[0px_1px_1px_rgba(0,0,0,0.05)] h-[64px] relative shrink-0 w-full" data-name="Header">
      <div className="flex flex-row items-center size-full">
        <div className="content-stretch flex items-center px-[24px] relative size-full">
          <BackgroundBorder />
        </div>
      </div>
    </div>
  );
}

function Container11() {
  return (
    <div className="gap-x-[24px] gap-y-[24px] grid grid-cols-[repeat(3,minmax(0,1fr))] grid-rows-[_128px] relative shrink-0 w-full" data-name="Container">
      <div className="bg-[#f2f4f6] col-1 h-[128px] justify-self-stretch relative rounded-[12px] row-1 shrink-0" data-name="Background" />
      <div className="bg-[#f2f4f6] col-2 h-[128px] justify-self-stretch relative rounded-[12px] row-1 shrink-0" data-name="Background" />
      <div className="bg-[#f2f4f6] col-3 h-[128px] justify-self-stretch relative rounded-[12px] row-1 shrink-0" data-name="Background" />
    </div>
  );
}

function Container10() {
  return (
    <div className="flex-[1_0_0] min-h-px relative w-full" data-name="Container">
      <div className="content-stretch flex flex-col gap-[24px] items-start p-[32px] relative size-full">
        <div className="bg-[#f2f4f6] h-[256px] relative rounded-[12px] shrink-0 w-full" data-name="Background" />
        <Container11 />
      </div>
    </div>
  );
}

function Main() {
  return (
    <div className="blur-[1px] content-stretch flex flex-[1_0_0] flex-col items-start min-h-[1024px] min-w-px opacity-40 relative self-stretch" data-name="Main">
      <Header />
      <Container10 />
    </div>
  );
}

function Heading2() {
  return (
    <div className="content-stretch flex flex-col items-center relative shrink-0 w-full" data-name="Heading 3">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center leading-[0] relative shrink-0 text-[#191c1e] text-[32px] text-center tracking-[-0.8px] uppercase whitespace-nowrap">
        <p className="leading-[40px] mb-0">TRONG HỆ THỐNG XUAT-COPILOT, TÁC NHÂN NÀO CHỊU</p>
        <p className="leading-[40px] mb-0">TRÁCH NHIỆM LẬP KẾ HOẠCH HÀNH ĐỘNG VÀ TẠO RA CÁC</p>
        <p className="leading-[40px]">LỆNH TƯƠNG TÁC CỤ THỂ?</p>
      </div>
    </div>
  );
}

function Container12() {
  return (
    <div className="content-stretch flex flex-col items-center relative shrink-0 w-full" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Italic',sans-serif] font-normal italic justify-center leading-[0] relative shrink-0 text-[#464554] text-[14px] text-center whitespace-nowrap">
        <p className="leading-[21px]">Deep dive into the XUAT-Copilot agent architecture and decision logic.</p>
      </div>
    </div>
  );
}

function MainQuestion() {
  return (
    <div className="content-stretch flex flex-col gap-[16px] items-start max-w-[896px] py-[32px] relative shrink-0 w-[896px]" data-name="Main Question">
      <Heading2 />
      <Container12 />
    </div>
  );
}

function Background1() {
  return (
    <div className="bg-[#ba1a1a] relative rounded-[9999px] shrink-0 size-[32px]" data-name="Background">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex items-center justify-center pb-[4.5px] pt-[3.5px] relative size-full">
        <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center leading-[0] relative shrink-0 text-[16px] text-center text-white whitespace-nowrap">
          <p className="leading-[24px]">A</p>
        </div>
      </div>
    </div>
  );
}

function Container14() {
  return (
    <div className="content-stretch flex items-start relative shrink-0 w-full" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:SemiBold',sans-serif] font-semibold justify-center leading-[0] relative shrink-0 text-[#191c1e] text-[16px] whitespace-nowrap">
        <p className="leading-[25.6px]">Mô-đun Nhận thức (Perception Module)</p>
      </div>
    </div>
  );
}

function Margin1() {
  return (
    <div className="content-stretch flex flex-col items-start pb-[8px] relative shrink-0 w-full" data-name="Margin">
      <Container14 />
    </div>
  );
}

function Container16() {
  return (
    <div className="relative shrink-0 size-[8.167px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 8.16667 8.16667">
        <g id="Container">
          <path d={svgPaths.p2317cf00} fill="var(--fill-0, #BA1A1A)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Container17() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center leading-[0] relative shrink-0 text-[#ba1a1a] text-[10px] tracking-[0.5px] uppercase whitespace-nowrap">
        <p className="leading-[15px]">CHƯA ĐÚNG LẮM!</p>
      </div>
    </div>
  );
}

function Overlay() {
  return (
    <div className="bg-[rgba(186,26,26,0.1)] content-stretch flex gap-[8px] items-center px-[8px] py-[4px] relative rounded-[9999px] shrink-0" data-name="Overlay">
      <Container16 />
      <Container17 />
    </div>
  );
}

function Container15() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0 w-full" data-name="Container">
      <Overlay />
    </div>
  );
}

function Margin2() {
  return (
    <div className="content-stretch flex flex-col h-[30.406px] items-start justify-end min-h-[23px] pt-[7.406px] relative shrink-0 w-full" data-name="Margin">
      <Container15 />
    </div>
  );
}

function Container13() {
  return (
    <div className="flex-[1_0_0] min-h-[64px] min-w-px relative" data-name="Container">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex flex-col gap-[0.004px] items-start min-h-[inherit] relative size-full">
        <Margin1 />
        <Margin2 />
      </div>
    </div>
  );
}

function OptionAIncorrect() {
  return (
    <div className="bg-[rgba(255,218,214,0.1)] col-1 h-[82px] justify-self-stretch relative rounded-[12px] row-1 shrink-0" data-name="Option A: Incorrect">
      <div aria-hidden className="absolute border border-[#ba1a1a] border-solid inset-0 pointer-events-none rounded-[12px]" />
      <div className="content-stretch flex gap-[16px] items-start p-[9px] relative size-full">
        <Background1 />
        <Container13 />
      </div>
    </div>
  );
}

function Container18() {
  return (
    <div className="h-[12.025px] relative shrink-0 w-[16.3px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 16.3 12.025">
        <g id="Container">
          <path d={svgPaths.p2f7dfa00} fill="var(--fill-0, white)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Background2() {
  return (
    <div className="bg-[#2e7d32] relative rounded-[9999px] shrink-0 size-[32px]" data-name="Background">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex items-center justify-center relative size-full">
        <Container18 />
      </div>
    </div>
  );
}

function Container20() {
  return (
    <div className="content-stretch flex items-start relative shrink-0 w-full" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:SemiBold',sans-serif] font-semibold justify-center leading-[0] relative shrink-0 text-[#191c1e] text-[16px] whitespace-nowrap">
        <p className="leading-[25.6px]">Tác nhân Vận hành (Operation Agent)</p>
      </div>
    </div>
  );
}

function Margin3() {
  return (
    <div className="content-stretch flex flex-col items-start pb-[8px] relative shrink-0 w-full" data-name="Margin">
      <Container20 />
    </div>
  );
}

function Container22() {
  return (
    <div className="relative shrink-0 size-[11.667px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 11.6667 11.6667">
        <g id="Container">
          <path d={svgPaths.p1d9bcc00} fill="var(--fill-0, #2E7D32)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Container23() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center leading-[0] relative shrink-0 text-[#2e7d32] text-[10px] tracking-[0.5px] uppercase whitespace-nowrap">
        <p className="leading-[15px]">CÂU TRẢ LỜI CHÍNH XÁC</p>
      </div>
    </div>
  );
}

function Overlay1() {
  return (
    <div className="bg-[rgba(46,125,50,0.1)] content-stretch flex gap-[8px] items-center px-[8px] py-[4px] relative rounded-[9999px] shrink-0" data-name="Overlay">
      <Container22 />
      <Container23 />
    </div>
  );
}

function Container21() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0 w-full" data-name="Container">
      <Overlay1 />
    </div>
  );
}

function Margin4() {
  return (
    <div className="content-stretch flex flex-col h-[30.406px] items-start justify-end min-h-[23px] pt-[7.406px] relative shrink-0 w-full" data-name="Margin">
      <Container21 />
    </div>
  );
}

function Container19() {
  return (
    <div className="flex-[1_0_0] min-h-[64px] min-w-px relative" data-name="Container">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex flex-col gap-[0.004px] items-start min-h-[inherit] relative size-full">
        <Margin3 />
        <Margin4 />
      </div>
    </div>
  );
}

function OptionBCorrect() {
  return (
    <div className="bg-[#e8f5e9] col-2 h-[82px] justify-self-stretch relative rounded-[12px] row-1 shrink-0" data-name="Option B: Correct">
      <div aria-hidden className="absolute border border-[#2e7d32] border-solid inset-0 pointer-events-none rounded-[12px]" />
      <div className="content-stretch flex gap-[16px] items-start p-[9px] relative size-full">
        <Background2 />
        <Container19 />
      </div>
    </div>
  );
}

function Border() {
  return (
    <div className="relative rounded-[9999px] shrink-0 size-[32px]" data-name="Border">
      <div aria-hidden className="absolute border border-[#c7c4d7] border-solid inset-0 pointer-events-none rounded-[9999px]" />
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex items-center justify-center pb-[4.5px] pt-[3.5px] px-px relative size-full">
        <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center leading-[0] relative shrink-0 text-[#464554] text-[16px] text-center whitespace-nowrap">
          <p className="leading-[24px]">C</p>
        </div>
      </div>
    </div>
  );
}

function Paragraph() {
  return (
    <div className="relative shrink-0 w-full" data-name="Paragraph">
      <div className="[word-break:break-word] content-stretch flex items-start justify-between leading-[0] relative size-full whitespace-nowrap">
        <div className="flex flex-col font-['Hanken_Grotesk:SemiBold',sans-serif] font-semibold justify-center relative shrink-0 text-[#191c1e] text-[16px]">
          <p className="leading-[25.6px]">Tác nhân Chọn tham số (Parameter Selection Agent)</p>
        </div>
        <div className="flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center relative shrink-0 text-[#464554] text-[14px]">
          <p className="leading-[20px]">C</p>
        </div>
      </div>
    </div>
  );
}

function Container24() {
  return (
    <div className="flex-[1_0_0] min-h-[64px] min-w-px relative" data-name="Container">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex flex-col items-start min-h-[inherit] pb-[38.41px] relative size-full">
        <Paragraph />
      </div>
    </div>
  );
}

function OptionCDefault() {
  return (
    <div className="col-1 h-[82px] justify-self-stretch relative rounded-[12px] row-2 shrink-0" data-name="Option C: Default">
      <div aria-hidden className="absolute border border-[rgba(199,196,215,0.3)] border-solid inset-0 pointer-events-none rounded-[12px]" />
      <div className="content-stretch flex gap-[16px] items-start p-[9px] relative size-full">
        <Border />
        <Container24 />
      </div>
    </div>
  );
}

function Border1() {
  return (
    <div className="relative rounded-[9999px] shrink-0 size-[32px]" data-name="Border">
      <div aria-hidden className="absolute border border-[#c7c4d7] border-solid inset-0 pointer-events-none rounded-[9999px]" />
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex items-center justify-center pb-[4.5px] pt-[3.5px] px-px relative size-full">
        <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center leading-[0] relative shrink-0 text-[#464554] text-[16px] text-center whitespace-nowrap">
          <p className="leading-[24px]">D</p>
        </div>
      </div>
    </div>
  );
}

function Paragraph1() {
  return (
    <div className="relative shrink-0 w-full" data-name="Paragraph">
      <div className="[word-break:break-word] content-stretch flex items-start justify-between leading-[0] relative size-full whitespace-nowrap">
        <div className="flex flex-col font-['Hanken_Grotesk:SemiBold',sans-serif] font-semibold justify-center relative shrink-0 text-[#191c1e] text-[16px]">
          <p className="leading-[25.6px]">Tác nhân Phân tích (Analysis Agent)</p>
        </div>
        <div className="flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center relative shrink-0 text-[#464554] text-[14px]">
          <p className="leading-[20px]">D</p>
        </div>
      </div>
    </div>
  );
}

function Container25() {
  return (
    <div className="flex-[1_0_0] min-h-[64px] min-w-px relative" data-name="Container">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex flex-col items-start min-h-[inherit] pb-[38.41px] relative size-full">
        <Paragraph1 />
      </div>
    </div>
  );
}

function OptionDDefault() {
  return (
    <div className="col-2 h-[82px] justify-self-stretch relative rounded-[12px] row-2 shrink-0" data-name="Option D: Default">
      <div aria-hidden className="absolute border border-[rgba(199,196,215,0.3)] border-solid inset-0 pointer-events-none rounded-[12px]" />
      <div className="content-stretch flex gap-[16px] items-start p-[9px] relative size-full">
        <Border1 />
        <Container25 />
      </div>
    </div>
  );
}

function OptionsContainer() {
  return (
    <div className="gap-x-[16px] gap-y-[16px] grid grid-cols-[repeat(2,minmax(0,1fr))] grid-rows-[__82px_82px] relative shrink-0 w-full" data-name="Options Container">
      <OptionAIncorrect />
      <OptionBCorrect />
      <OptionCDefault />
      <OptionDDefault />
    </div>
  );
}

function Overlay2() {
  return (
    <div className="h-[36px] relative shrink-0 w-[32px]" data-name="Overlay">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 32 36">
        <g id="Overlay">
          <rect fill="var(--fill-0, #4648D4)" fillOpacity="0.1" height="36" rx="8" width="32" />
          <path d={svgPaths.p2e997c80} fill="var(--fill-0, #4648D4)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Heading3() {
  return (
    <div className="content-stretch flex flex-col items-start mb-[-0.625px] relative shrink-0 w-full" data-name="Heading 4">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Regular',sans-serif] font-normal justify-center leading-[0] relative shrink-0 text-[#191c1e] text-[16px] whitespace-nowrap">
        <p className="leading-[24px]">Academic Integrity</p>
      </div>
    </div>
  );
}

function Container28() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0 w-full" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Regular',sans-serif] font-normal justify-center leading-[0] relative shrink-0 text-[#464554] text-[14px] whitespace-nowrap">
        <p className="leading-[22.75px] mb-0">AI-generated quiz content is grounded in your provided documents to ensure accuracy and prevent hallucinations. Our system</p>
        <p className="leading-[22.75px]">{`cross-references lecture notes and textbooks uploaded to your "Machine Learning" notebook.`}</p>
      </div>
    </div>
  );
}

function Container27() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0 w-[787.81px]" data-name="Container">
      <Heading3 />
      <Container28 />
    </div>
  );
}

function Container26() {
  return (
    <div className="relative shrink-0 w-full" data-name="Container">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex gap-[16px] items-start relative size-full">
        <Overlay2 />
        <Container27 />
      </div>
    </div>
  );
}

function IntegritySectionInsideScrollable() {
  return (
    <div className="bg-[#eceef0] relative rounded-[12px] shrink-0 w-full" data-name="Integrity Section (Inside Scrollable)">
      <div aria-hidden className="absolute border border-[rgba(199,196,215,0.2)] border-solid inset-0 pointer-events-none rounded-[12px]" />
      <div className="content-stretch flex flex-col items-start p-[25px] relative size-full">
        <Container26 />
      </div>
    </div>
  );
}

function ModalScrollableContent() {
  return (
    <div className="absolute content-stretch flex flex-col gap-[48px] items-center left-0 overflow-auto p-[48px] right-0 top-[105px]" data-name="Modal Scrollable Content">
      <MainQuestion />
      <OptionsContainer />
      <IntegritySectionInsideScrollable />
    </div>
  );
}

function Container29() {
  return (
    <div className="h-[12.75px] relative shrink-0 w-[11.25px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 11.25 12.75">
        <g id="Container">
          <path d={svgPaths.pe32d800} fill="var(--fill-0, #464554)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Button() {
  return (
    <div className="relative rounded-[8px] shrink-0" data-name="Button">
      <div aria-hidden className="absolute border border-[#c7c4d7] border-solid inset-0 pointer-events-none rounded-[8px]" />
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex gap-[8px] items-center justify-center px-[25px] py-[9px] relative size-full">
        <Container29 />
        <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Regular',sans-serif] font-normal justify-center leading-[0] relative shrink-0 text-[#464554] text-[16px] text-center whitespace-nowrap">
          <p className="leading-[24px]">Report Issue</p>
        </div>
      </div>
    </div>
  );
}

function Button1() {
  return (
    <div className="content-stretch flex flex-col items-center justify-center px-[24px] py-[8px] relative shrink-0" data-name="Button">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Regular',sans-serif] font-normal justify-center leading-[0] relative shrink-0 text-[#4648d4] text-[16px] text-center whitespace-nowrap">
        <p className="leading-[24px]">Previous</p>
      </div>
    </div>
  );
}

function Container31() {
  return (
    <div className="relative shrink-0 size-[12px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 12 12">
        <g id="Container">
          <path d={svgPaths.p304eaa0} fill="var(--fill-0, white)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Button2() {
  return (
    <div className="bg-[#4648d4] content-stretch drop-shadow-[0px_4px_10px_rgba(0,0,0,0.05)] flex gap-[8px] items-center justify-center px-[32px] py-[8px] relative rounded-[8px] shrink-0" data-name="Button">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Regular',sans-serif] font-normal justify-center leading-[0] relative shrink-0 text-[16px] text-center text-white whitespace-nowrap">
        <p className="leading-[24px]">Next Question</p>
      </div>
      <Container31 />
    </div>
  );
}

function Container30() {
  return (
    <div className="relative shrink-0" data-name="Container">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex gap-[24px] items-center relative size-full">
        <Button1 />
        <Button2 />
      </div>
    </div>
  );
}

function ModalFooter() {
  return (
    <div className="absolute bg-[#f2f4f6] content-stretch flex items-center justify-between left-0 pb-[24px] pt-[25px] px-[32px] right-0 rounded-bl-[12px] rounded-br-[12px] top-[817.5px]" data-name="Modal Footer">
      <div aria-hidden className="absolute border-[rgba(199,196,215,0.2)] border-solid border-t inset-0 pointer-events-none rounded-bl-[12px] rounded-br-[12px]" />
      <Button />
      <Container30 />
    </div>
  );
}

function Heading1() {
  return (
    <div className="content-stretch flex flex-col items-start mb-[-0.01px] relative shrink-0 w-full" data-name="Heading 2">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:SemiBold',sans-serif] font-semibold justify-center leading-[0] relative shrink-0 text-[#191c1e] text-[24px] whitespace-nowrap">
        <p className="leading-[31.2px]">Machine Learning Concepts Quiz</p>
      </div>
    </div>
  );
}

function Container34() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:SemiBold',sans-serif] font-semibold justify-center leading-[0] relative shrink-0 text-[#464554] text-[12px] tracking-[0.6px] whitespace-nowrap">
        <p className="leading-[12px]">Machine Learning</p>
      </div>
    </div>
  );
}

function Container35() {
  return (
    <div className="h-[7px] relative shrink-0 w-[4.317px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 4.31667 7">
        <g id="Container">
          <path d={svgPaths.p35022f90} fill="var(--fill-0, #464554)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function Container36() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:SemiBold',sans-serif] font-semibold justify-center leading-[0] relative shrink-0 text-[#4648d4] text-[12px] tracking-[0.6px] whitespace-nowrap">
        <p className="leading-[12px]">Week 4 Evaluation</p>
      </div>
    </div>
  );
}

function Container33() {
  return (
    <div className="content-stretch flex gap-[8px] items-center relative shrink-0 w-full" data-name="Container">
      <Container34 />
      <Container35 />
      <Container36 />
    </div>
  );
}

function Container32() {
  return (
    <div className="relative shrink-0 w-[356.05px]" data-name="Container">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex flex-col items-start relative size-full">
        <Heading1 />
        <Container33 />
      </div>
    </div>
  );
}

function Container39() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center leading-[0] relative shrink-0 text-[#464554] text-[10px] uppercase whitespace-nowrap">
        <p className="leading-[15px]">PROGRESS</p>
      </div>
    </div>
  );
}

function Container40() {
  return (
    <div className="content-stretch flex flex-col items-start relative shrink-0" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Regular',sans-serif] font-normal justify-center leading-[0] relative shrink-0 text-[#191c1e] text-[16px] whitespace-nowrap">
        <p className="leading-[16px]">Question 1/10</p>
      </div>
    </div>
  );
}

function Container38() {
  return (
    <div className="content-stretch flex flex-col items-end relative shrink-0" data-name="Container">
      <Container39 />
      <Container40 />
    </div>
  );
}

function Svg() {
  return (
    <div className="h-full overflow-clip relative w-[40px]" data-name="SVG">
      <div className="absolute inset-[5%]" data-name="Vector">
        <div className="absolute inset-[-4.17%]">
          <svg className="block size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 39 39">
            <path d={svgPaths.p20100480} id="Vector" stroke="var(--stroke-0, #C7C4D7)" strokeOpacity="0.3" strokeWidth="3" />
          </svg>
        </div>
      </div>
      <div className="absolute inset-[5%]" data-name="Vector">
        <div className="absolute inset-[-4.17%]">
          <svg className="block size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 39 39">
            <path d={svgPaths.p20100480} id="Vector" stroke="var(--stroke-0, #4648D4)" strokeWidth="3" />
          </svg>
        </div>
      </div>
    </div>
  );
}

function Container42() {
  return (
    <div className="absolute content-stretch flex inset-0 items-center justify-center" data-name="Container">
      <div className="[word-break:break-word] flex flex-col font-['Hanken_Grotesk:Bold',sans-serif] font-bold justify-center leading-[0] relative shrink-0 text-[#4648d4] text-[10px] text-center whitespace-nowrap">
        <p className="leading-[15px]">10%</p>
      </div>
    </div>
  );
}

function Container41() {
  return (
    <div className="content-stretch flex flex-col items-start justify-center relative shrink-0 size-[40px]" data-name="Container">
      <div className="flex h-[40px] items-center justify-center relative shrink-0 w-full" style={{ containerType: "size" }}>
        <div className="-rotate-90 flex-none h-[100cqw]">
          <Svg />
        </div>
      </div>
      <Container42 />
    </div>
  );
}

function Background3() {
  return (
    <div className="bg-[#e6e8ea] content-stretch flex gap-[16px] items-center px-[24px] py-[8px] relative rounded-[8px] shrink-0" data-name="Background">
      <Container38 />
      <Container41 />
    </div>
  );
}

function Container43() {
  return (
    <div className="relative shrink-0 size-[14px]" data-name="Container">
      <svg className="absolute block inset-0 size-full" fill="none" preserveAspectRatio="none" viewBox="0 0 14 14">
        <g id="Container">
          <path d={svgPaths.p15494480} fill="var(--fill-0, #464554)" id="Icon" />
        </g>
      </svg>
    </div>
  );
}

function ButtonCloseModal() {
  return (
    <div className="content-stretch flex items-center justify-center relative rounded-[9999px] shrink-0 size-[40px]" data-name="Button - Close modal">
      <Container43 />
    </div>
  );
}

function Container37() {
  return (
    <div className="relative shrink-0" data-name="Container">
      <div className="bg-clip-padding border-0 border-[transparent] border-solid content-stretch flex gap-[32px] items-center relative size-full">
        <Background3 />
        <ButtonCloseModal />
      </div>
    </div>
  );
}

function ModalHeader() {
  return (
    <div className="absolute bg-white content-stretch flex items-center justify-between left-0 pb-[25px] pt-[24px] px-[32px] right-0 rounded-tl-[12px] rounded-tr-[12px] top-[11.75px]" data-name="Modal Header">
      <div aria-hidden className="absolute border-[rgba(199,196,215,0.2)] border-b border-solid inset-0 pointer-events-none rounded-tl-[12px] rounded-tr-[12px]" />
      <Container32 />
      <Container37 />
    </div>
  );
}

function ModalDialog() {
  return (
    <div className="bg-white drop-shadow-[0px_4px_10px_rgba(0,0,0,0.05)] h-[908.5px] max-h-[972.7999877929688px] max-w-[1024px] relative rounded-[12px] shrink-0 w-[1024px]" data-name="Modal Dialog">
      <ModalScrollableContent />
      <ModalFooter />
      <ModalHeader />
    </div>
  );
}

function ModalOverlay() {
  return (
    <div className="absolute backdrop-blur-[2px] bg-[rgba(25,28,30,0.6)] content-stretch flex inset-0 items-center justify-center overflow-auto pb-[95.25px] pt-[20.25px] px-[32px]" data-name="Modal Overlay">
      <ModalDialog />
    </div>
  );
}

export default function HtmlBody() {
  return (
    <div className="content-stretch flex items-start justify-center pl-[256px] relative size-full" style={{ backgroundImage: "linear-gradient(90deg, rgb(248, 250, 252) 0%, rgb(248, 250, 252) 100%), linear-gradient(90deg, rgb(255, 255, 255) 0%, rgb(255, 255, 255) 100%)" }} data-name="Html → Body">
      <AsideBackgroundDashboardContextBlurred />
      <Main />
      <ModalOverlay />
    </div>
  );
}