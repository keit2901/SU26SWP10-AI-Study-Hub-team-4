import React, { useState } from 'react';
import { X, Flag, ArrowRight, ShieldCheck, CheckCircle2, XCircle } from 'lucide-react';
import { motion, AnimatePresence } from 'motion/react';
import { ImageWithFallback } from './components/figma/ImageWithFallback';

interface Option {
  id: string;
  text: string;
  isCorrect?: boolean;
}

interface Question {
  id: number;
  title: string;
  breadcrumb: string;
  question: string;
  subtitle: string;
  options: Option[];
}

const QUIZ_DATA: Question[] = [
  {
    id: 1,
    title: "Machine Learning Concepts Quiz",
    breadcrumb: "Machine Learning > Week 4 Evaluation",
    question: "TRONG HỆ THỐNG XUAT-COPILOT, TÁC NHÂN NÀO CHỊU TRÁCH NHIỆM LẬP KẾ HOẠCH HÀNH ĐỘNG VÀ TẠO RA CÁC LỆNH TƯƠNG TÁC CỤ THỂ?",
    subtitle: "Deep dive into the XUAT-Copilot agent architecture and decision logic.",
    options: [
      { id: 'A', text: "Mô-đun Nhận thức (Perception Module)" },
      { id: 'B', text: "Tác nhân Vận hành (Operation Agent)", isCorrect: true },
      { id: 'C', text: "Tác nhân Chọn tham số (Parameter Selection Agent)" },
      { id: 'D', text: "Tác nhân Phân tích (Analysis Agent)" },
    ]
  }
];

export default function App() {
  const [currentQuestionIndex, setCurrentQuestionIndex] = useState(0);
  const [selectedOptionId, setSelectedOptionId] = useState<string | null>('A'); // Defaulting to 'A' as in the screenshot for visual match
  const [isSubmitted, setIsSubmitted] = useState(true); // Defaulting to true as in the screenshot for visual match

  const currentQuestion = QUIZ_DATA[currentQuestionIndex];
  const progress = ((currentQuestionIndex + 1) / QUIZ_DATA.length) * 10; // Simple calc for UI match

  const handleOptionClick = (id: string) => {
    if (isSubmitted) return;
    setSelectedOptionId(id);
  };

  const handleNext = () => {
    if (currentQuestionIndex < QUIZ_DATA.length - 1) {
      setCurrentQuestionIndex(prev => prev + 1);
      setSelectedOptionId(null);
      setIsSubmitted(false);
    }
  };

  const handlePrevious = () => {
    if (currentQuestionIndex > 0) {
      setCurrentQuestionIndex(prev => prev - 1);
      setSelectedOptionId(null);
      setIsSubmitted(false);
    }
  };

  return (
    <div className="min-h-screen bg-[#111111]/40 flex items-center justify-center p-4 font-['Hanken_Grotesk']">
      <div className="bg-white rounded-2xl w-full max-w-5xl shadow-2xl overflow-hidden flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="px-8 py-6 border-b border-gray-100 flex items-center justify-between shrink-0">
          <div>
            <h1 className="text-[20px] font-bold text-[#191C1E]">{currentQuestion.title}</h1>
            <p className="text-[12px] text-[#4648D4] font-medium mt-1 uppercase tracking-wide">
              {currentQuestion.breadcrumb.split('>').map((part, i, arr) => (
                <span key={part}>
                  {part.trim()}
                  {i < arr.length - 1 && <span className="mx-1 text-gray-400">&gt;</span>}
                </span>
              ))}
            </p>
          </div>

          <div className="flex items-center gap-6">
            <div className="bg-[#F2F4F6] rounded-lg px-4 py-2 flex items-center gap-4">
              <div className="text-right">
                <p className="text-[10px] text-gray-500 font-bold uppercase tracking-widest">Progress</p>
                <p className="text-[14px] font-bold text-[#191C1E]">Question {currentQuestion.id}/{QUIZ_DATA.length * 10}</p>
              </div>
              <div className="relative size-10">
                <svg className="size-full" viewBox="0 0 36 36">
                  <path
                    className="stroke-[#E8E8E8]"
                    strokeWidth="3"
                    fill="none"
                    d="M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831"
                  />
                  <path
                    className="stroke-[#4648D4]"
                    strokeWidth="3"
                    strokeDasharray="10, 100"
                    strokeLinecap="round"
                    fill="none"
                    d="M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831"
                  />
                </svg>
                <div className="absolute inset-0 flex items-center justify-center text-[10px] font-bold text-[#4648D4]">
                  10%
                </div>
              </div>
            </div>
            <button className="text-gray-400 hover:text-gray-600 transition-colors">
              <X size={24} />
            </button>
          </div>
        </div>

        {/* Content - Scrollable */}
        <div className="flex-1 overflow-y-auto px-12 py-10 flex flex-col items-center">
          <div className="max-w-4xl w-full">
            {/* Question Text */}
            <div className="text-center mb-12">
              <h2 className="text-[28px] md:text-[32px] font-bold text-[#191C1E] leading-tight mb-4 uppercase">
                {currentQuestion.question}
              </h2>
              <p className="text-[14px] text-[#464554] italic">
                {currentQuestion.subtitle}
              </p>
            </div>

            {/* Options Grid */}
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-12">
              {currentQuestion.options.map((option) => {
                const isSelected = selectedOptionId === option.id;
                const showFeedback = isSubmitted && isSelected;
                const isCorrect = option.isCorrect;
                
                // Special case for visual match: show B as correct even if not selected if A is selected and submitted
                const forceShowBAsCorrect = isSubmitted && selectedOptionId === 'A' && option.id === 'B';
                
                let borderColor = "border-[#C7C4D7]/30";
                let bgColor = "bg-white";
                let circleBg = "bg-white border border-[#C7C4D7]";
                let circleText = "text-[#464554]";
                let feedbackText = null;
                let feedbackColor = "";

                if (isSelected) {
                  if (isSubmitted) {
                    if (isCorrect) {
                      borderColor = "border-[#2E7D32]";
                      bgColor = "bg-[#E8F5E9]";
                      circleBg = "bg-[#2E7D32]";
                      circleText = "text-white";
                      feedbackText = "CÂU TRẢ LỜI CHÍNH XÁC";
                      feedbackColor = "text-[#2E7D32] bg-[#2E7D32]/10";
                    } else {
                      borderColor = "border-[#BA1A1A]";
                      bgColor = "bg-[#FFDADB]/10";
                      circleBg = "bg-[#BA1A1A]";
                      circleText = "text-white";
                      feedbackText = "CHƯA ĐÚNG LẮM!";
                      feedbackColor = "text-[#BA1A1A] bg-[#BA1A1A]/10";
                    }
                  } else {
                    borderColor = "border-[#4648D4]";
                    bgColor = "bg-[#4648D4]/5";
                  }
                }

                if (forceShowBAsCorrect) {
                  borderColor = "border-[#2E7D32]";
                  bgColor = "bg-[#E8F5E9]";
                  circleBg = "bg-[#2E7D32]";
                  circleText = "text-white";
                  feedbackText = "CÂU TRẢ LỜI CHÍNH XÁC";
                  feedbackColor = "text-[#2E7D32] bg-[#2E7D32]/10";
                }

                return (
                  <button
                    key={option.id}
                    onClick={() => handleOptionClick(option.id)}
                    className={`relative p-4 rounded-xl border text-left transition-all group flex gap-4 ${borderColor} ${bgColor}`}
                  >
                    <div className={`size-8 shrink-0 rounded-full flex items-center justify-center font-bold text-[16px] transition-colors ${circleBg} ${circleText}`}>
                      {isSubmitted && (isCorrect || (forceShowBAsCorrect && option.id === 'B')) ? <CheckCircle2 size={16} /> : option.id}
                    </div>
                    
                    <div className="flex-1 flex flex-col justify-between">
                      <div className="flex justify-between items-start">
                        <span className="text-[16px] font-semibold text-[#191C1E] pr-8 leading-tight">
                          {option.text}
                        </span>
                        {!isSubmitted && (
                           <span className="text-[14px] font-bold text-[#464554] opacity-0 group-hover:opacity-100 transition-opacity">
                             {option.id}
                           </span>
                        )}
                        {isSubmitted && !isSelected && !forceShowBAsCorrect && (
                           <span className="text-[14px] font-bold text-[#464554]">
                             {option.id}
                           </span>
                        )}
                      </div>
                      
                      {feedbackText && (
                        <div className={`mt-3 inline-flex items-center gap-2 px-2 py-1 rounded-full text-[10px] font-bold tracking-wider ${feedbackColor}`}>
                          {isCorrect || forceShowBAsCorrect ? <CheckCircle2 size={10} /> : <XCircle size={10} />}
                          {feedbackText}
                        </div>
                      )}
                    </div>
                  </button>
                );
              })}
            </div>

            {/* Integrity Box */}
            <div className="bg-[#ECEEF0] rounded-xl p-6 border border-[#C7C4D7]/20 flex gap-4">
              <div className="size-8 bg-[#4648D4]/10 rounded-lg flex items-center justify-center shrink-0">
                <ShieldCheck className="text-[#4648D4]" size={20} />
              </div>
              <div>
                <h3 className="text-[16px] font-medium text-[#191C1E] mb-1">Academic Integrity</h3>
                <p className="text-[14px] text-[#464554] leading-relaxed">
                  AI-generated quiz content is grounded in your provided documents to ensure accuracy and prevent hallucinations. Our system cross-references lecture notes and textbooks uploaded to your "Machine Learning" notebook.
                </p>
              </div>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="px-8 py-6 border-t border-gray-100 flex items-center justify-between bg-white shrink-0">
          <button className="px-6 py-2.5 border border-[#C7C4D7] rounded-lg text-[#464554] text-[16px] flex items-center gap-2 hover:bg-gray-50 transition-colors">
            <Flag size={18} />
            Report Issue
          </button>

          <div className="flex items-center gap-4">
            <button 
              onClick={handlePrevious}
              disabled={currentQuestionIndex === 0}
              className="px-6 py-2.5 text-[#4648D4] text-[16px] font-medium hover:bg-[#4648D4]/5 rounded-lg transition-colors disabled:opacity-50"
            >
              Previous
            </button>
            <button 
              onClick={handleNext}
              className="px-6 py-2.5 bg-[#4648D4] text-white rounded-lg text-[16px] font-medium flex items-center gap-2 hover:bg-[#3B3DBA] transition-colors"
            >
              Next Question
              <ArrowRight size={20} />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
